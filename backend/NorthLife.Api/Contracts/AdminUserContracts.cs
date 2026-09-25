using System.Text.Json;
using NorthLife.Api.Models;

namespace NorthLife.Api.Contracts;

public sealed record AdminUserSummary(
    Guid Id,
    string FullName,
    string Email,
    string BusinessName,
    UserRole Role,
    bool EmailConfirmed,
    bool TotpEnabled,
    bool Suspended,
    DateTimeOffset CreatedAt,
    int PublishedEvents,
    int PendingEvents);

public sealed record AdminUserPage(IReadOnlyList<AdminUserSummary> Items, string? NextCursor);

public sealed record AdminUserEvent(Guid Id, string Title, EventStatus Status, DateTimeOffset StartAt);

public sealed record AuditEntryResponse(
    Guid Id,
    Guid ActorId,
    string ActorName,
    string Action,
    string TargetType,
    Guid TargetId,
    JsonElement Details,
    DateTimeOffset CreatedAt);

public sealed record AdminUserDetails(
    AdminUserSummary User,
    string Phone,
    DateTimeOffset? SuspendedAt,
    string? SuspensionReason,
    bool GoogleLinked,
    IReadOnlyList<AdminUserEvent> Events,
    IReadOnlyList<AuditEntryResponse> Audit);

public sealed record AuditPage(IReadOnlyList<AuditEntryResponse> Items, string? NextCursor);

public sealed record SuspendUserRequest(string Reason);
public sealed record ChangeRoleRequest(UserRole Role);
