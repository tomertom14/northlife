namespace NorthLife.Api.Models;

/// <summary>Links an external identity (provider + subject) to a NorthLife account.</summary>
public sealed class ExternalLogin
{
    public required string Provider { get; set; }
    public required string Subject { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }

    public AppUser User { get; set; } = null!;
}
