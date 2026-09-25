using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NorthLife.Api.Contracts;
using NorthLife.Api.Data;
using NorthLife.Api.Email;
using NorthLife.Api.Identity;
using NorthLife.Api.Models;
using Npgsql;
using System.ComponentModel.DataAnnotations;

namespace NorthLife.Api.Authentication;

public abstract record SignInOutcome;
public sealed record SignedIn(AuthResponse Response) : SignInOutcome;
public sealed record SecondFactorRequired(string Ticket) : SignInOutcome;
public sealed record ProfileRequired(string Ticket, string Email, string FullName) : SignInOutcome;

public sealed class AuthService(
    AppDbContext dbContext,
    IPasswordHasher<AppUser> passwordHasher,
    AuthTokenService tokenService,
    TimeProvider timeProvider,
    IdentityTickets tickets,
    TotpService totp,
    UserTokenService userTokens,
    AccountEmails emails,
    IGoogleTokenValidator google)
{
    public const string GoogleProvider = "google";

    public async Task<AuthResponse> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        AuthInputValidator.ValidateRegistration(request);
        var normalizedEmail = NormalizeEmail(request.Email);

        if (await dbContext.Users.AnyAsync(
                user => user.NormalizedEmail == normalizedEmail,
                cancellationToken))
        {
            throw new AuthConflictException("email_exists", "כבר קיים חשבון עם כתובת האימייל הזו.");
        }

        var user = new AppUser
        {
            Id = Guid.CreateVersion7(),
            FullName = request.FullName.Trim(),
            Email = request.Email.Trim(),
            NormalizedEmail = normalizedEmail,
            PasswordHash = string.Empty,
            Phone = request.Phone.Trim(),
            BusinessName = request.BusinessName.Trim(),
            Role = UserRole.BusinessOwner,
            CreatedAtUtc = timeProvider.GetUtcNow(),
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        dbContext.Users.Add(user);
        await SaveNewAccountAsync(cancellationToken);

        var token = await userTokens.IssueAsync(user.Id, UserTokenPurpose.VerifyEmail, cancellationToken);
        await emails.SendVerificationAsync(user.Email, user.FullName, token, cancellationToken);
        return tokenService.Create(user);
    }

    public async Task<SignInOutcome> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email?.Trim() ?? string.Empty;
        var password = request.Password ?? string.Empty;
        if (email.Length == 0 || password.Length == 0)
        {
            throw new LoginFailedException();
        }

        var normalizedEmail = NormalizeEmail(email);
        var user = await dbContext.Users.SingleOrDefaultAsync(
            candidate => candidate.NormalizedEmail == normalizedEmail,
            cancellationToken);
        if (user is null || user.PasswordHash.Length == 0)
        {
            // Spend the same hashing work as a real account so timing does not reveal registered emails.
            passwordHasher.VerifyHashedPassword(TimingDummy, TimingDummyHash.Value, password);
            throw new LoginFailedException();
        }

        var verification = passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            password);
        if (verification == PasswordVerificationResult.Failed)
        {
            throw new LoginFailedException();
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, password);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return BeginSession(user);
    }

    /// <summary>Second step of sign-in: a TOTP code or a backup code for the ticket's account.</summary>
    public async Task<AuthResponse> CompleteMfaAsync(MfaRequest request, CancellationToken cancellationToken)
    {
        var userId = tickets.ReadMfa(request.MfaToken) ?? throw new LoginFailedException("mfa_expired");
        var user = await dbContext.Users.SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken)
            ?? throw new LoginFailedException("mfa_expired");
        if (user.Suspended) throw new AccountSuspendedException();
        if (!await totp.VerifySecondFactorAsync(user, request.Code ?? string.Empty, cancellationToken))
        {
            throw new LoginFailedException("invalid_code");
        }

        return tokenService.Create(user, mfaVerified: true);
    }

    public async Task<SignInOutcome> GoogleSignInAsync(string idToken, CancellationToken cancellationToken)
    {
        if (!google.Enabled) throw new GoogleSignInException("google_disabled");
        var identity = await google.ValidateAsync(idToken, cancellationToken)
            ?? throw new GoogleSignInException("invalid_google_token");

        var linked = await dbContext.ExternalLogins
            .Include(login => login.User)
            .SingleOrDefaultAsync(
                login => login.Provider == GoogleProvider && login.Subject == identity.Subject,
                cancellationToken);
        var normalizedEmail = NormalizeEmail(identity.Email);
        var existing = linked?.User ?? await dbContext.Users.SingleOrDefaultAsync(
            user => user.NormalizedEmail == normalizedEmail,
            cancellationToken);

        switch (GoogleAccountResolver.Decide(linked is not null, existing is not null, identity.EmailVerified))
        {
            case GoogleSignInDecision.SignInLinked:
                return BeginSession(existing!);
            case GoogleSignInDecision.LinkExisting:
                dbContext.ExternalLogins.Add(new ExternalLogin
                {
                    Provider = GoogleProvider,
                    Subject = identity.Subject,
                    UserId = existing!.Id,
                    CreatedAtUtc = timeProvider.GetUtcNow(),
                });
                existing.EmailConfirmedAtUtc ??= timeProvider.GetUtcNow();
                await dbContext.SaveChangesAsync(cancellationToken);
                return BeginSession(existing);
            case GoogleSignInDecision.RequireProfile:
                return new ProfileRequired(tickets.IssueSignup(identity), identity.Email, identity.Name);
            default:
                throw new GoogleSignInException("google_email_unverified");
        }
    }

    /// <summary>Creates the business account for a Google identity once the profile is complete.</summary>
    public async Task<AuthResponse> CompleteGoogleSignupAsync(GoogleCompleteRequest request, CancellationToken cancellationToken)
    {
        var identity = tickets.ReadSignup(request.SignupToken) ?? throw new GoogleSignInException("signup_expired");
        AuthInputValidator.ValidateProfile(request.FullName, request.BusinessName, request.Phone);
        var normalizedEmail = NormalizeEmail(identity.Email);
        if (await dbContext.Users.AnyAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken) ||
            await dbContext.ExternalLogins.AnyAsync(
                login => login.Provider == GoogleProvider && login.Subject == identity.Subject,
                cancellationToken))
        {
            throw new AuthConflictException("email_exists", "כבר קיים חשבון עם כתובת האימייל הזו.");
        }

        var now = timeProvider.GetUtcNow();
        var user = new AppUser
        {
            Id = Guid.CreateVersion7(),
            FullName = request.FullName.Trim(),
            Email = identity.Email.Trim(),
            NormalizedEmail = normalizedEmail,
            PasswordHash = string.Empty,
            Phone = request.Phone.Trim(),
            BusinessName = request.BusinessName.Trim(),
            Role = UserRole.BusinessOwner,
            CreatedAtUtc = now,
            EmailConfirmedAtUtc = now,
        };
        dbContext.Users.Add(user);
        dbContext.ExternalLogins.Add(new ExternalLogin
        {
            Provider = GoogleProvider,
            Subject = identity.Subject,
            UserId = user.Id,
            CreatedAtUtc = now,
        });
        await SaveNewAccountAsync(cancellationToken);
        return tokenService.Create(user);
    }

    internal static string NormalizeEmail(string email) =>
        email.Trim().ToUpperInvariant();

    private SignInOutcome BeginSession(AppUser user)
    {
        // Only reached after the credential check, so this does not reveal whether an account exists.
        if (user.Suspended) throw new AccountSuspendedException();
        return user.TotpEnabled
            ? new SecondFactorRequired(tickets.IssueMfa(user.Id))
            : new SignedIn(tokenService.Create(user));
    }

    private async Task SaveNewAccountAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new AuthConflictException("email_exists", "כבר קיים חשבון עם כתובת האימייל הזו.");
        }
    }

    private static readonly AppUser TimingDummy = new()
    {
        FullName = string.Empty,
        Email = string.Empty,
        NormalizedEmail = string.Empty,
        PasswordHash = string.Empty,
        Phone = string.Empty,
        BusinessName = string.Empty,
    };

    private static readonly Lazy<string> TimingDummyHash = new(() =>
        new PasswordHasher<AppUser>().HashPassword(TimingDummy, Guid.NewGuid().ToString("N")));
}

public static class AuthInputValidator
{
    public const string PasswordRule = "הסיסמה חייבת להכיל 10–128 תווים, אות גדולה, אות קטנה ומספר.";

    public static void ValidateRegistration(RegisterRequest request)
    {
        var errors = ProfileErrors(request.FullName, request.BusinessName, request.Phone);

        var email = request.Email?.Trim() ?? string.Empty;
        if (email.Length > 320 || !new EmailAddressAttribute().IsValid(email))
        {
            errors["email"] = ["יש להזין כתובת אימייל תקינה."];
        }

        if (!IsStrongPassword(request.Password))
        {
            errors["password"] = [PasswordRule];
        }

        if (errors.Count > 0)
        {
            throw new AuthValidationException(errors);
        }
    }

    public static void ValidateProfile(string? fullName, string? businessName, string? phone)
    {
        var errors = ProfileErrors(fullName, businessName, phone);
        if (errors.Count > 0) throw new AuthValidationException(errors);
    }

    public static bool IsStrongPassword(string? password) =>
        password is { Length: >= 10 and <= 128 } &&
        password.Any(char.IsUpper) &&
        password.Any(char.IsLower) &&
        password.Any(char.IsDigit);

    private static Dictionary<string, string[]> ProfileErrors(string? fullName, string? businessName, string? phone)
    {
        var errors = new Dictionary<string, string[]>();
        AddLength(errors, "fullName", fullName, 2, 150, "שם מלא");
        AddLength(errors, "businessName", businessName, 2, 200, "שם העסק");
        AddLength(errors, "phone", phone, 7, 30, "טלפון");
        return errors;
    }

    private static void AddLength(
        Dictionary<string, string[]> errors,
        string field,
        string? value,
        int minimum,
        int maximum,
        string label)
    {
        var length = value?.Trim().Length ?? 0;
        if (length < minimum || length > maximum)
        {
            errors[field] = [$"{label} חייב להכיל {minimum}–{maximum} תווים."];
        }
    }
}

public sealed class AuthValidationException(
    IReadOnlyDictionary<string, string[]> errors) : Exception
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class AuthConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class LoginFailedException(string code = "invalid_credentials") : Exception(code)
{
    public string Code { get; } = code;
}

public sealed class AccountSuspendedException : Exception;

public sealed class GoogleSignInException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
