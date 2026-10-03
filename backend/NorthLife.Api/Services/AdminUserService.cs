using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NorthLife.Api.Authentication;
using NorthLife.Api.Contracts;
using NorthLife.Api.Data;
using NorthLife.Api.Models;
using Npgsql;

namespace NorthLife.Api.Services;

/// <summary>Pure guard rules for account administration, unit-tested on their own.</summary>
public static class AdminUserRules
{
    public static string? SuspendProblem(Guid actorId, AppUser target, int activeAdmins)
    {
        if (target.Id == actorId) return "cannot_suspend_self";
        if (target.Suspended) return "already_suspended";
        if (target.Role == UserRole.Admin && activeAdmins <= 1) return "last_admin";
        return null;
    }

    public static string? RoleChangeProblem(Guid actorId, AppUser target, UserRole newRole, int activeAdmins)
    {
        if (target.Id == actorId) return "cannot_change_own_role";
        if (target.Role == newRole) return "no_change";
        if (target.Role == UserRole.Admin && !target.Suspended && activeAdmins <= 1) return "last_admin";
        return null;
    }
}

public sealed class AdminUserService(
    AppDbContext dbContext,
    AuditLog audit,
    ISessionValidator sessions,
    TimeProvider timeProvider)
{
    public const int DefaultPageSize = 20;
    public const int MaximumPageSize = 50;

    public async Task<AdminUserPage> ListAsync(
        string? search,
        UserRole? role,
        string? status,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        pageSize = Math.Clamp(pageSize, 1, MaximumPageSize);
        var query = dbContext.Users.AsNoTracking();

        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            if (term.Length > 100) throw new AdminUserRuleException("search_too_long");
            var pattern = $"%{term}%";
            query = query.Where(user =>
                EF.Functions.ILike(user.FullName, pattern) ||
                EF.Functions.ILike(user.Email, pattern) ||
                EF.Functions.ILike(user.BusinessName, pattern));
        }

        if (role is not null) query = query.Where(user => user.Role == role);
        query = status switch
        {
            "suspended" => query.Where(user => user.SuspendedAtUtc != null),
            "active" => query.Where(user => user.SuspendedAtUtc == null),
            "unverified" => query.Where(user => user.EmailConfirmedAtUtc == null),
            _ => query,
        };

        // Seek past the previous page with a row-value comparison on the (created_at, id) index.
        if (KeysetCursor.Decode(cursor) is { } position)
        {
            query = query.Where(user =>
                EF.Functions.LessThan(
                    ValueTuple.Create(user.CreatedAtUtc, user.Id),
                    ValueTuple.Create(position.CreatedAt, position.Id)));
        }

        var rows = await query
            .OrderByDescending(user => user.CreatedAtUtc)
            .ThenByDescending(user => user.Id)
            .Take(pageSize + 1)
            .Select(user => new AdminUserSummary(
                user.Id,
                user.FullName,
                user.Email,
                user.BusinessName,
                user.Role,
                user.EmailConfirmedAtUtc != null,
                user.TotpEnabledAtUtc != null,
                user.SuspendedAtUtc != null,
                user.CreatedAtUtc,
                user.Events.Count(eventItem => eventItem.Status == EventStatus.Published),
                user.Events.Count(eventItem => eventItem.Status == EventStatus.Pending)))
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > pageSize;
        var items = rows.Take(pageSize).ToList();
        var next = hasMore ? new KeysetCursor(items[^1].CreatedAt, items[^1].Id).Encode() : null;
        return new AdminUserPage(items, next);
    }

    public async Task<AdminUserDetails?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
        if (user is null) return null;

        var events = await dbContext.Events.AsNoTracking()
            .Where(eventItem => eventItem.OwnerId == userId)
            .OrderByDescending(eventItem => eventItem.StartAtUtc)
            .Take(50)
            .Select(eventItem => new AdminUserEvent(eventItem.Id, eventItem.Title, eventItem.Status, eventItem.StartAtUtc))
            .ToListAsync(cancellationToken);
        // Count across all of the user's events, not only the 50 listed.
        var counts = await dbContext.Events.AsNoTracking()
            .Where(eventItem => eventItem.OwnerId == userId)
            .GroupBy(eventItem => eventItem.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.Status, group => group.Count, cancellationToken);
        var published = counts.GetValueOrDefault(EventStatus.Published);
        var pending = counts.GetValueOrDefault(EventStatus.Pending);
        var googleLinked = await dbContext.ExternalLogins.AnyAsync(login => login.UserId == userId, cancellationToken);
        var auditEntries = await ProjectAudit(dbContext.AuditEntries.AsNoTracking()
                .Where(entry => entry.TargetId == userId || entry.ActorId == userId)
                .OrderByDescending(entry => entry.CreatedAtUtc)
                .ThenByDescending(entry => entry.Id)
                .Take(20))
            .ToListAsync(cancellationToken);

        return new AdminUserDetails(
            new AdminUserSummary(
                user.Id, user.FullName, user.Email, user.BusinessName, user.Role, user.EmailConfirmed,
                user.TotpEnabled, user.Suspended, user.CreatedAtUtc, published, pending),
            user.Phone,
            user.SuspendedAtUtc,
            user.SuspensionReason,
            googleLinked,
            events,
            auditEntries.Select(ToResponse).ToList());
    }

    public Task SuspendAsync(Guid actorId, Guid userId, string? reason, CancellationToken cancellationToken)
    {
        var trimmed = reason?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > 500) throw new AdminUserRuleException("reason_required");

        return ChangeAsync(actorId, userId, cancellationToken, (target, activeAdmins) =>
        {
            var problem = AdminUserRules.SuspendProblem(actorId, target, activeAdmins);
            if (problem is not null) throw new AdminUserRuleException(problem);
            target.SuspendedAtUtc = timeProvider.GetUtcNow();
            target.SuspensionReason = trimmed;
            target.RotateSecurityStamp();
            audit.Record(actorId, "user.suspended", "User", target.Id, new { reason = trimmed });
        });
    }

    public Task UnsuspendAsync(Guid actorId, Guid userId, CancellationToken cancellationToken) =>
        ChangeAsync(actorId, userId, cancellationToken, (target, _) =>
        {
            if (!target.Suspended) throw new AdminUserRuleException("not_suspended");
            var previousReason = target.SuspensionReason;
            target.SuspendedAtUtc = null;
            target.SuspensionReason = null;
            target.RotateSecurityStamp();
            audit.Record(actorId, "user.unsuspended", "User", target.Id, new { previousReason });
        });

    public Task ChangeRoleAsync(Guid actorId, Guid userId, UserRole role, CancellationToken cancellationToken) =>
        ChangeAsync(actorId, userId, cancellationToken, (target, activeAdmins) =>
        {
            var problem = AdminUserRules.RoleChangeProblem(actorId, target, role, activeAdmins);
            if (problem is not null) throw new AdminUserRuleException(problem);
            var before = target.Role;
            target.Role = role;
            target.RotateSecurityStamp();
            audit.Record(actorId, "user.role_changed", "User", target.Id, new { from = before.ToString(), to = role.ToString() });
        });

    public async Task<AuditPage> AuditAsync(string? cursor, int pageSize, CancellationToken cancellationToken)
    {
        pageSize = Math.Clamp(pageSize, 1, MaximumPageSize);
        var query = dbContext.AuditEntries.AsNoTracking();
        if (KeysetCursor.Decode(cursor) is { } position)
        {
            query = query.Where(entry =>
                EF.Functions.LessThan(
                    ValueTuple.Create(entry.CreatedAtUtc, entry.Id),
                    ValueTuple.Create(position.CreatedAt, position.Id)));
        }

        var rows = await ProjectAudit(query
                .OrderByDescending(entry => entry.CreatedAtUtc)
                .ThenByDescending(entry => entry.Id)
                .Take(pageSize + 1))
            .ToListAsync(cancellationToken);
        var items = rows.Take(pageSize).Select(ToResponse).ToList();
        var next = rows.Count > pageSize ? new KeysetCursor(items[^1].CreatedAt, items[^1].Id).Encode() : null;
        return new AuditPage(items, next);
    }

    /// <summary>
    /// Runs a guarded change under SERIALIZABLE isolation, so two administrators acting at the same
    /// moment cannot, for example, both demote each other and leave no active admin.
    /// </summary>
    private async Task ChangeAsync(
        Guid actorId,
        Guid userId,
        CancellationToken cancellationToken,
        Action<AppUser, int> change)
    {
        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var target = await dbContext.Users.SingleOrDefaultAsync(user => user.Id == userId, cancellationToken)
                ?? throw new AdminUserNotFoundException();
            var activeAdmins = await dbContext.Users.CountAsync(
                user => user.Role == UserRole.Admin && user.SuspendedAtUtc == null,
                cancellationToken);
            change(target, activeAdmins);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            sessions.Invalidate(userId);
        }
        catch (Exception exception) when (IsSerializationFailure(exception))
        {
            throw new AdminUserRuleException("concurrent_change");
        }
    }

    private static bool IsSerializationFailure(Exception exception) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure } ||
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure };

    // Filter and order on the entity first; EF cannot translate predicates over a constructor projection.
    // A null actor is the automatic event approval service.
    private static IQueryable<AuditRow> ProjectAudit(IQueryable<AuditEntry> source) =>
        source.Select(entry => new AuditRow(
            entry.Id, entry.ActorId, entry.Actor == null ? null : entry.Actor.FullName, entry.Action, entry.TargetType,
            entry.TargetId, entry.Details, entry.CreatedAtUtc));

    private static AuditEntryResponse ToResponse(AuditRow row)
    {
        using var details = JsonDocument.Parse(row.Details);
        return new AuditEntryResponse(
            row.Id, row.ActorId, row.ActorName, row.Action, row.TargetType, row.TargetId,
            details.RootElement.Clone(), row.CreatedAtUtc);
    }

    private sealed record AuditRow(
        Guid Id, Guid? ActorId, string? ActorName, string Action, string TargetType,
        Guid TargetId, string Details, DateTimeOffset CreatedAtUtc);
}

public sealed class AdminUserRuleException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public sealed class AdminUserNotFoundException : Exception;
