using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NorthLife.Api.Data;
using NorthLife.Api.Email;
using NorthLife.Api.Identity;
using NorthLife.Api.Models;

namespace NorthLife.Api.Authentication;

/// <summary>Email ownership and password recovery flows.</summary>
public sealed class AccountService(
    AppDbContext dbContext,
    IPasswordHasher<AppUser> passwordHasher,
    UserTokenService userTokens,
    AccountEmails emails,
    TimeProvider timeProvider)
{
    public async Task<bool> VerifyEmailAsync(string? token, CancellationToken cancellationToken)
    {
        var user = await userTokens.RedeemAsync(token, UserTokenPurpose.VerifyEmail, cancellationToken);
        if (user is null) return false;
        user.EmailConfirmedAtUtc ??= timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task ResendVerificationAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
        if (user is null || user.EmailConfirmed) return;
        var token = await userTokens.IssueAsync(user.Id, UserTokenPurpose.VerifyEmail, cancellationToken);
        await emails.SendVerificationAsync(user.Email, user.FullName, token, cancellationToken);
    }

    /// <summary>Sends a reset link when the account exists; callers always answer the same way.</summary>
    public async Task ForgotPasswordAsync(string? email, CancellationToken cancellationToken)
    {
        var normalized = AuthService.NormalizeEmail(email ?? string.Empty);
        if (normalized.Length is 0 or > 320) return;
        var user = await dbContext.Users.SingleOrDefaultAsync(candidate => candidate.NormalizedEmail == normalized, cancellationToken);
        if (user is null) return;
        var token = await userTokens.IssueAsync(user.Id, UserTokenPurpose.ResetPassword, cancellationToken);
        await emails.SendPasswordResetAsync(user.Email, user.FullName, token, cancellationToken);
    }

    /// <summary>A reset link also proves the address, so it confirms the email.</summary>
    public async Task<bool> ResetPasswordAsync(string? token, string? password, CancellationToken cancellationToken)
    {
        if (!AuthInputValidator.IsStrongPassword(password))
        {
            throw new AuthValidationException(new Dictionary<string, string[]>
            {
                ["password"] = [AuthInputValidator.PasswordRule],
            });
        }

        var user = await userTokens.RedeemAsync(token, UserTokenPurpose.ResetPassword, cancellationToken);
        if (user is null) return false;
        user.PasswordHash = passwordHasher.HashPassword(user, password!);
        user.EmailConfirmedAtUtc ??= timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
