namespace NorthLife.Api.Models;

/// <summary>Append-only record of an administrator action.</summary>
public sealed class AuditEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ActorId { get; set; }
    public required string Action { get; set; }
    public required string TargetType { get; set; }
    public Guid TargetId { get; set; }

    /// <summary>JSON describing what changed (before/after values, reasons).</summary>
    public required string Details { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }

    public AppUser Actor { get; set; } = null!;
}
