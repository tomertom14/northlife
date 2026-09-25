using Microsoft.EntityFrameworkCore;
using NorthLife.Api.Data;
using NorthLife.Api.Models;

namespace NorthLife.Api.Identity;

/// <summary>Issues and redeems single-use emailed tokens; the database holds only SHA-256 hashes.</summary>
public sealed class UserTokenService(AppDbContext dbContext, TimeProvider timeProvider)
{
    public static readonly TimeSpan VerifyEmailLifetime = TimeSpan.FromHours(24);
    public static readonly TimeSpan ResetPasswordLifetime = TimeSpan.FromMinutes(30);

    /// <summary>Retires earlier unused tokens for the same purpose, so only the newest link works.</summary>
    public async Task<string> IssueAsync(Guid userId, UserTokenPurpose purpose, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await dbContext.UserTokens
            .Where(token => token.UserId == userId && token.Purpose == purpose && token.UsedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.UsedAtUtc, now), cancellationToken);

        var (value, hash) = SecureTokens.NewLinkToken();
        dbContext.UserTokens.Add(new UserToken
        {
            UserId = userId,
            Purpose = purpose,
            TokenHash = hash,
            CreatedAtUtc = now,
            ExpiresAtUtc = now + (purpose == UserTokenPurpose.VerifyEmail ? VerifyEmailLifetime : ResetPasswordLifetime),
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return value;
    }

    /// <summary>
    /// Marks the token used with one conditional UPDATE, so two concurrent requests cannot both
    /// redeem it. Returns the owner, or null for unknown, expired or already-used tokens.
    /// </summary>
    public async Task<AppUser?> RedeemAsync(string? token, UserTokenPurpose purpose, CancellationToken cancellationToken)
    {
        var value = token?.Trim() ?? string.Empty;
        if (value.Length is 0 or > 100) return null;

        var hash = SecureTokens.Hash(value);
        var now = timeProvider.GetUtcNow();
        var redeemed = await dbContext.UserTokens
            .Where(candidate =>
                candidate.TokenHash == hash &&
                candidate.Purpose == purpose &&
                candidate.UsedAtUtc == null &&
                candidate.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.UsedAtUtc, now), cancellationToken);
        if (redeemed == 0) return null;

        var userId = await dbContext.UserTokens
            .Where(candidate => candidate.TokenHash == hash)
            .Select(candidate => candidate.UserId)
            .SingleAsync(cancellationToken);
        return await dbContext.Users.SingleAsync(user => user.Id == userId, cancellationToken);
    }
}
