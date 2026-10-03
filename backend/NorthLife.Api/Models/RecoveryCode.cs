namespace NorthLife.Api.Models;

/// <summary>A single-use two-factor backup code. Only its SHA-256 hash is stored.</summary>
public sealed class RecoveryCode
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public required string CodeHash { get; set; }
    public DateTimeOffset? UsedAtUtc { get; set; }

    public AppUser User { get; set; } = null!;
}
