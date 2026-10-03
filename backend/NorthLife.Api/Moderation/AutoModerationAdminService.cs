using Microsoft.EntityFrameworkCore;
using NorthLife.Api.Contracts;
using NorthLife.Api.Data;
using NorthLife.Api.Models;
using NorthLife.Api.Services;

namespace NorthLife.Api.Moderation;

/// <summary>What the admin page reads and changes: the settings, the next run and the recent runs.</summary>
public sealed class AutoModerationAdminService(AppDbContext dbContext, AuditLog audit, TimeProvider timeProvider)
{
    public const int RecentRuns = 10;

    public async Task<AutoModerationOverview> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var settings = await dbContext.AutoModerationSettings.AsNoTracking().SingleAsync(cancellationToken);
        var runs = await (
                from run in dbContext.AutoModerationRuns.AsNoTracking()
                join user in dbContext.Users.AsNoTracking() on run.TriggeredById equals (Guid?)user.Id into admins
                from admin in admins.DefaultIfEmpty()
                orderby run.StartedAtUtc descending
                select new AutoModerationRunResponse(
                    run.Id, run.Trigger, admin == null ? null : admin.FullName, run.Mode, run.StartedAtUtc, run.FinishedAtUtc,
                    run.Checked, run.Approved, run.WouldApprove, run.Held, run.Error != null))
            .Take(RecentRuns)
            .ToListAsync(cancellationToken);

        var nextRun = settings.Mode == AutoModerationMode.Off
            ? (DateTimeOffset?)null
            : AutoModerationSchedule.NextRun(timeProvider.GetUtcNow(), settings.RunAtLocal, settings.LastScheduledRunDate);
        return new AutoModerationOverview(ToRequest(settings), settings.UpdatedAtUtc, nextRun, runs);
    }

    /// <summary>Validates and saves the settings; the audit entry lists each changed field with its old and new value.</summary>
    public async Task<AutoModerationOverview> UpdateSettingsAsync(
        Guid adminId,
        AutoModerationSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var next = AutoModerationSettingsValidator.Validate(request);
        var settings = await dbContext.AutoModerationSettings.SingleAsync(cancellationToken);

        var changes = new Dictionary<string, object>();
        void Compare<T>(string field, T before, T after)
        {
            if (!EqualityComparer<T>.Default.Equals(before, after)) changes[field] = new { from = before, to = after };
        }

        Compare("mode", settings.Mode.ToString(), next.Mode.ToString());
        Compare("runAt", settings.RunAtLocal.ToString("HH:mm"), next.RunAtLocal.ToString("HH:mm"));
        Compare("minAccountAgeDays", settings.MinAccountAgeDays, next.MinAccountAgeDays);
        Compare("minApprovedEvents", settings.MinApprovedEvents, next.MinApprovedEvents);
        Compare("rejectionLookbackDays", settings.RejectionLookbackDays, next.RejectionLookbackDays);
        Compare("maxAutoApprovalsPerOwnerPerDay", settings.MaxAutoApprovalsPerOwnerPerDay, next.MaxAutoApprovalsPerOwnerPerDay);
        Compare("maxDaysAhead", settings.MaxDaysAhead, next.MaxDaysAhead);
        Compare("maxDurationDays", settings.MaxDurationDays, next.MaxDurationDays);
        Compare("maxPrice", settings.MaxPrice, next.MaxPrice);
        Compare("duplicateSimilarity", settings.DuplicateSimilarity, next.DuplicateSimilarity);
        Compare("duplicateDistanceMeters", settings.DuplicateDistanceMeters, next.DuplicateDistanceMeters);
        var added = next.BannedWords.Except(settings.BannedWords, StringComparer.Ordinal).ToArray();
        var removed = settings.BannedWords.Except(next.BannedWords, StringComparer.Ordinal).ToArray();
        if (added.Length > 0 || removed.Length > 0) changes["bannedWords"] = new { added, removed };

        if (changes.Count > 0)
        {
            settings.Mode = next.Mode;
            settings.RunAtLocal = next.RunAtLocal;
            settings.MinAccountAgeDays = next.MinAccountAgeDays;
            settings.MinApprovedEvents = next.MinApprovedEvents;
            settings.RejectionLookbackDays = next.RejectionLookbackDays;
            settings.MaxAutoApprovalsPerOwnerPerDay = next.MaxAutoApprovalsPerOwnerPerDay;
            settings.MaxDaysAhead = next.MaxDaysAhead;
            settings.MaxDurationDays = next.MaxDurationDays;
            settings.MaxPrice = next.MaxPrice;
            settings.DuplicateSimilarity = next.DuplicateSimilarity;
            settings.DuplicateDistanceMeters = next.DuplicateDistanceMeters;
            settings.BannedWords = next.BannedWords;
            settings.UpdatedAtUtc = timeProvider.GetUtcNow();
            audit.Record(adminId, "automoderation.settings_changed", "AutoModeration", settings.Id, new { changes });
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await GetOverviewAsync(cancellationToken);
    }

    public static AutoModerationSettingsRequest ToRequest(AutoModerationSettings settings) => new(
        settings.Mode,
        settings.RunAtLocal.ToString("HH:mm"),
        settings.MinAccountAgeDays,
        settings.MinApprovedEvents,
        settings.RejectionLookbackDays,
        settings.MaxAutoApprovalsPerOwnerPerDay,
        settings.MaxDaysAhead,
        settings.MaxDurationDays,
        settings.MaxPrice,
        settings.DuplicateSimilarity,
        settings.DuplicateDistanceMeters,
        settings.BannedWords);
}
