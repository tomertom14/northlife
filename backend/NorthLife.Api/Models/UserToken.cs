namespace NorthLife.Api.Models;

public enum UserTokenPurpose
{
    VerifyEmail,
    ResetPassword,
}

/// <summary>A single-use emailed token. Only its SHA-256 hash is stored.</summary>
public sealed class UserToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public UserTokenPurpose Purpose { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? UsedAtUtc { get; set; }

    public AppUser User { get; set; } = null!;
}
