using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NorthLife.Api.Contracts;
using NorthLife.Api.Data;
using NorthLife.Api.Models;
using Npgsql;
using System.ComponentModel.DataAnnotations;

namespace NorthLife.Api.Authentication;

public sealed class AuthService(
    AppDbContext dbContext,
    IPasswordHasher<AppUser> passwordHasher,
    AuthTokenService tokenService,
    TimeProvider timeProvider)
{
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

        return tokenService.Create(user);
    }

    public async Task<AuthResponse> LoginAsync(
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
        if (user is null)
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

        return tokenService.Create(user);
    }

    internal static string NormalizeEmail(string email) =>
        email.Trim().ToUpperInvariant();

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
    public static void ValidateRegistration(RegisterRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        AddLength(errors, "fullName", request.FullName, 2, 150, "שם מלא");
        AddLength(errors, "businessName", request.BusinessName, 2, 200, "שם העסק");
        AddLength(errors, "phone", request.Phone, 7, 30, "טלפון");

        var email = request.Email?.Trim() ?? string.Empty;
        if (email.Length > 320 || !new EmailAddressAttribute().IsValid(email))
        {
            errors["email"] = ["יש להזין כתובת אימייל תקינה."];
        }

        var password = request.Password ?? string.Empty;
        var validPassword =
            password.Length is >= 10 and <= 128 &&
            password.Any(char.IsUpper) &&
            password.Any(char.IsLower) &&
            password.Any(char.IsDigit);
        if (!validPassword)
        {
            errors["password"] =
            [
                "הסיסמה חייבת להכיל 10–128 תווים, אות גדולה, אות קטנה ומספר."
            ];
        }

        if (errors.Count > 0)
        {
            throw new AuthValidationException(errors);
        }
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

public sealed class LoginFailedException : Exception;
