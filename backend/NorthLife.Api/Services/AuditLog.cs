using System.Text.Json;
using NorthLife.Api.Data;
using NorthLife.Api.Models;

namespace NorthLife.Api.Services;

/// <summary>Adds audit entries to the current unit of work; they commit with the change they describe.</summary>
public sealed class AuditLog(AppDbContext dbContext, TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <param name="actorId">The acting administrator, or null for the automatic event approval service.</param>
    public void Record(Guid? actorId, string action, string targetType, Guid targetId, object details) =>
        dbContext.AuditEntries.Add(new AuditEntry
        {
            ActorId = actorId,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Details = JsonSerializer.Serialize(details, Json),
            CreatedAtUtc = timeProvider.GetUtcNow(),
        });
}
