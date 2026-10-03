using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NorthLife.Api.Data;
using NorthLife.Api.Models;
using NorthLife.Api.Recommendations;
using NorthLife.Api.Services;

namespace NorthLife.Api.Moderation;

public sealed class AutoModerationOptions
{
    public const string SectionName = "AutoModeration";

    /// <summary>Runs the background worker that starts the daily run; tests without a database switch it off.</summary>
    public bool WorkerEnabled { get; set; } = true;

    /// <summary>How often the worker checks whether the daily run is due.</summary>
    public int TickSeconds { get; set; } = 300;

    /// <summary>Pending events looked at per run, oldest first; the rest wait for the next run.</summary>
    public int MaxEventsPerRun { get; set; } = 500;
}

public sealed record AutoModerationRunSummary(Guid RunId, AutoModerationMode Mode, int Checked, int Approved, int WouldApprove, int Held);

/// <summary>
/// The automatic event approval service. A run reads every pending event, checks the four terms
/// (<see cref="ModerationRules"/>), approves the events that pass (in <see cref="AutoModerationMode.Approve"/>
/// mode), and writes a decision for every event so the admin queue can show why an event is still waiting.
/// It never rejects.
/// </summary>
public sealed class AutoModerationService(
    AppDbContext dbContext,
    EventLifecycleService lifecycle,
    AuditLog audit,
    TimeProvider timeProvider,
    IOptions<AutoModerationOptions> options,
    ILogger<AutoModerationService> logger)
{
    /// <summary>PostgreSQL advisory lock that keeps two runs from overlapping, across app instances.</summary>
    private const long RunLockKey = 0x4E4C_4D4F_4430_3137;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<AutoModerationSettings> GetSettingsAsync(CancellationToken cancellationToken) =>
        dbContext.AutoModerationSettings.AsNoTracking().SingleAsync(cancellationToken);

    /// <summary>
    /// Starts the daily run when it is due. Today's run is claimed with a compare-and-set UPDATE, so
    /// however many app instances or worker ticks ask, exactly one of them runs it. If the run fails
    /// or another run holds the lock, the claim is handed back and the next check tries again.
    /// </summary>
    public async Task<AutoModerationRunSummary?> RunScheduledIfDueAsync(CancellationToken cancellationToken)
    {
        var settings = await GetSettingsAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (settings.Mode == AutoModerationMode.Off ||
            !AutoModerationSchedule.IsDue(now, settings.RunAtLocal, settings.LastScheduledRunDate))
        {
            return null;
        }

        var today = AutoModerationSchedule.LocalDate(now);
        var claimed = await dbContext.AutoModerationSettings
            .Where(row => row.Id == AutoModerationSettings.SingletonId &&
                          (row.LastScheduledRunDate == null || row.LastScheduledRunDate < today))
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.LastScheduledRunDate, today), cancellationToken);
        if (claimed == 0) return null;

        var released = false;
        try
        {
            var summary = await RunAsync(AutoModerationTrigger.Scheduled, null, cancellationToken);
            if (summary is null)
            {
                await ReleaseClaimAsync(today, settings.LastScheduledRunDate);
                released = true;
            }

            return summary;
        }
        catch when (!released)
        {
            await ReleaseClaimAsync(today, settings.LastScheduledRunDate);
            throw;
        }
    }

    /// <summary>"Run now" from the admin page. Returns null when another run is in progress.</summary>
    public async Task<AutoModerationRunSummary?> RunManualAsync(Guid adminId, CancellationToken cancellationToken)
    {
        var settings = await GetSettingsAsync(cancellationToken);
        if (settings.Mode == AutoModerationMode.Off) throw new AutoModerationOffException();

        var summary = await RunAsync(AutoModerationTrigger.Manual, adminId, cancellationToken);
        if (summary is null) return null;

        audit.Record(adminId, "automoderation.run", "AutoModeration", summary.RunId, new
        {
            mode = summary.Mode.ToString(),
            @checked = summary.Checked,
            approved = summary.Approved,
            wouldApprove = summary.WouldApprove,
            held = summary.Held,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return summary;
    }

    private async Task<AutoModerationRunSummary?> RunAsync(
        AutoModerationTrigger trigger,
        Guid? adminId,
        CancellationToken cancellationToken)
    {
        // The advisory lock belongs to the database session, so the whole run keeps one connection open.
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var locked = await dbContext.Database
                .SqlQuery<bool>($"SELECT pg_try_advisory_lock({RunLockKey}) AS \"Value\"")
                .SingleAsync(cancellationToken);
            if (!locked) return null;

            try
            {
                return await RunLockedAsync(trigger, adminId, cancellationToken);
            }
            finally
            {
                await dbContext.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({RunLockKey})", CancellationToken.None);
            }
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private async Task<AutoModerationRunSummary> RunLockedAsync(
        AutoModerationTrigger trigger,
        Guid? adminId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        // Holding the lock proves no run is in progress anywhere, so an unfinished run was cut off.
        await dbContext.AutoModerationRuns
            .Where(row => row.FinishedAtUtc == null)
            .ExecuteUpdateAsync(set => set
                .SetProperty(row => row.FinishedAtUtc, now)
                .SetProperty(row => row.Error, "Interrupted before it finished."), cancellationToken);

        var settings = await GetSettingsAsync(cancellationToken);
        var run = new AutoModerationRun
        {
            Trigger = trigger,
            TriggeredById = adminId,
            Mode = settings.Mode,
            StartedAtUtc = now,
        };
        dbContext.AutoModerationRuns.Add(run);
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();

        try
        {
            var summary = await ProcessAsync(run.Id, settings, now, cancellationToken);
            await dbContext.AutoModerationRuns
                .Where(row => row.Id == run.Id)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(row => row.FinishedAtUtc, timeProvider.GetUtcNow())
                    .SetProperty(row => row.Checked, summary.Checked)
                    .SetProperty(row => row.Approved, summary.Approved)
                    .SetProperty(row => row.WouldApprove, summary.WouldApprove)
                    .SetProperty(row => row.Held, summary.Held), cancellationToken);
            logger.LogInformation(
                "Automatic approval run {RunId} ({Trigger}, {Mode}): {Checked} checked, {Approved} approved, {WouldApprove} would approve, {Held} held.",
                run.Id, trigger, settings.Mode, summary.Checked, summary.Approved, summary.WouldApprove, summary.Held);
            return summary;
        }
        catch (Exception exception)
        {
            dbContext.ChangeTracker.Clear();
            var message = exception.Message.Length > 2000 ? exception.Message[..2000] : exception.Message;
            await dbContext.AutoModerationRuns
                .Where(row => row.Id == run.Id)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(row => row.FinishedAtUtc, timeProvider.GetUtcNow())
                    .SetProperty(row => row.Error, message), CancellationToken.None);
            throw;
        }
    }

    private async Task<AutoModerationRunSummary> ProcessAsync(
        Guid runId,
        AutoModerationSettings settings,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Evaluation reads snapshots; an approval later reloads the event and checks its revision.
        var pending = await dbContext.Events.AsNoTracking()
            .Where(eventItem => eventItem.Status == EventStatus.Pending)
            .OrderBy(eventItem => eventItem.UpdatedAtUtc)
            .Take(Math.Max(1, options.Value.MaxEventsPerRun))
            .ToListAsync(cancellationToken);
        if (pending.Count == 0) return new AutoModerationRunSummary(runId, settings.Mode, 0, 0, 0, 0);

        var owners = await LoadOwnersAsync(pending.Select(eventItem => eventItem.OwnerId).Distinct().ToList(), settings, now, cancellationToken);
        var (model, sameDay) = await LoadDuplicateCorpusAsync(pending, now, cancellationToken);

        int approved = 0, wouldApprove = 0, held = 0;
        var passedByOwner = new Dictionary<Guid, int>();
        foreach (var eventItem in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var subject = ToDuplicateCandidate(eventItem);
            var duplicate = DuplicateFinder.Find(
                subject,
                sameDay.GetValueOrDefault(subject.LocalStartDate) ?? [],
                model.Cosine,
                settings.DuplicateSimilarity,
                settings.DuplicateDistanceMeters);
            var passedEarlier = passedByOwner.GetValueOrDefault(eventItem.OwnerId);
            var reasons = ModerationRules.Evaluate(ToCandidate(eventItem), owners[eventItem.OwnerId], duplicate, settings, now, passedEarlier);

            if (reasons.Count > 0)
            {
                await RecordAsync(runId, eventItem.Id, eventItem.Revision, AutoModerationOutcome.Held, reasons, cancellationToken);
                held++;
            }
            else if (settings.Mode != AutoModerationMode.Approve)
            {
                await RecordAsync(runId, eventItem.Id, eventItem.Revision, AutoModerationOutcome.WouldApprove, [], cancellationToken);
                passedByOwner[eventItem.OwnerId] = passedEarlier + 1;
                wouldApprove++;
            }
            else if (await TryApproveAsync(runId, eventItem.Id, eventItem.Revision, cancellationToken))
            {
                passedByOwner[eventItem.OwnerId] = passedEarlier + 1;
                approved++;
            }
            else
            {
                held++;
            }
        }

        return new AutoModerationRunSummary(runId, settings.Mode, pending.Count, approved, wouldApprove, held);
    }

    /// <summary>
    /// Approves one event on a freshly loaded copy with the revision that was evaluated. If the owner
    /// edited or deleted it in the meantime, the lifecycle check or the revision concurrency token
    /// refuses, and the event is held instead. The approval, its audit entry and the decision commit
    /// together.
    /// </summary>
    private async Task<bool> TryApproveAsync(Guid runId, Guid eventId, int evaluatedRevision, CancellationToken cancellationToken)
    {
        try
        {
            var eventItem = await dbContext.Events.SingleOrDefaultAsync(candidate => candidate.Id == eventId, cancellationToken)
                ?? throw new EventLifecycleException("The event was deleted during the check.");
            lifecycle.Approve(eventItem, evaluatedRevision);
            audit.Record(null, "event.approved", "Event", eventItem.Id, new
            {
                eventItem.Title,
                from = EventStatus.Pending.ToString(),
                to = eventItem.Status.ToString(),
                automatic = true,
                runId,
            });
            dbContext.AutoModerationDecisions.Add(Decision(runId, eventItem.Id, eventItem.Revision, AutoModerationOutcome.Approved, []));
            await dbContext.SaveChangesAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            return true;
        }
        catch (Exception exception) when (exception is EventRevisionConflictException or EventLifecycleException or DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            await RecordAsync(runId, eventId, evaluatedRevision, AutoModerationOutcome.Held, [new ModerationReason("changed_during_check")], cancellationToken);
            return false;
        }
    }

    private async Task RecordAsync(
        Guid runId,
        Guid eventId,
        int revision,
        AutoModerationOutcome outcome,
        IReadOnlyList<ModerationReason> reasons,
        CancellationToken cancellationToken)
    {
        dbContext.AutoModerationDecisions.Add(Decision(runId, eventId, revision, outcome, reasons));
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
    }

    private AutoModerationDecision Decision(Guid runId, Guid eventId, int revision, AutoModerationOutcome outcome, IReadOnlyList<ModerationReason> reasons) => new()
    {
        RunId = runId,
        EventId = eventId,
        EventRevision = revision,
        Outcome = outcome,
        Reasons = JsonSerializer.Serialize(reasons, Json),
        DecidedAtUtc = timeProvider.GetUtcNow(),
    };

    /// <summary>The owner facts behind the first term, read in four grouped queries for all owners at once.</summary>
    private async Task<Dictionary<Guid, OwnerRecord>> LoadOwnersAsync(
        List<Guid> ownerIds,
        AutoModerationSettings settings,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var users = await dbContext.Users.AsNoTracking()
            .Where(user => ownerIds.Contains(user.Id))
            .Select(user => new { user.Id, user.CreatedAtUtc, user.EmailConfirmedAtUtc, user.SuspendedAtUtc })
            .ToListAsync(cancellationToken);

        var published = await dbContext.Events.AsNoTracking()
            .Where(eventItem => ownerIds.Contains(eventItem.OwnerId) && eventItem.Status == EventStatus.Published)
            .GroupBy(eventItem => eventItem.OwnerId)
            .Select(group => new { OwnerId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.OwnerId, row => row.Count, cancellationToken);

        // Rejections come from the audit log: a rejected event that is edited returns to Pending.
        // Deleted events count too, so deleting a rejected event does not clear the record.
        var lookbackStart = now - TimeSpan.FromDays(settings.RejectionLookbackDays);
        var lastRejection = await (
                from entry in dbContext.AuditEntries.AsNoTracking()
                join eventItem in dbContext.Events.IgnoreQueryFilters().AsNoTracking() on entry.TargetId equals eventItem.Id
                where entry.TargetType == "Event" && entry.Action == "event.rejected" &&
                      entry.CreatedAtUtc >= lookbackStart && ownerIds.Contains(eventItem.OwnerId)
                group entry by eventItem.OwnerId into rejections
                select new { OwnerId = rejections.Key, Last = rejections.Max(entry => entry.CreatedAtUtc) })
            .ToDictionaryAsync(row => row.OwnerId, row => row.Last, cancellationToken);

        var israelMidnight = AutoModerationSchedule.RunInstant(AutoModerationSchedule.LocalDate(now), TimeOnly.MinValue);
        var automaticToday = await (
                from entry in dbContext.AuditEntries.AsNoTracking()
                join eventItem in dbContext.Events.IgnoreQueryFilters().AsNoTracking() on entry.TargetId equals eventItem.Id
                where entry.TargetType == "Event" && entry.Action == "event.approved" && entry.ActorId == null &&
                      entry.CreatedAtUtc >= israelMidnight && ownerIds.Contains(eventItem.OwnerId)
                group entry by eventItem.OwnerId into approvals
                select new { OwnerId = approvals.Key, Count = approvals.Count() })
            .ToDictionaryAsync(row => row.OwnerId, row => row.Count, cancellationToken);

        return users.ToDictionary(
            user => user.Id,
            user => new OwnerRecord(
                user.CreatedAtUtc,
                user.EmailConfirmedAtUtc != null,
                user.SuspendedAtUtc != null,
                published.GetValueOrDefault(user.Id),
                lastRejection.TryGetValue(user.Id, out var rejectedAt) ? rejectedAt : null,
                automaticToday.GetValueOrDefault(user.Id)));
    }

    /// <summary>
    /// The events a pending event could duplicate: published or pending events that have not ended.
    /// The TF-IDF model is built over all of them plus the pending events, so word weights reflect
    /// the whole catalogue; the candidates are grouped by Israel start date for the same-day filter.
    /// </summary>
    private async Task<(TfIdfModel Model, Dictionary<DateOnly, List<DuplicateCandidate>> SameDay)> LoadDuplicateCorpusAsync(
        List<Event> pending,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var active = await dbContext.Events.AsNoTracking()
            .Where(eventItem => (eventItem.Status == EventStatus.Published || eventItem.Status == EventStatus.Pending) && eventItem.EndAtUtc > now)
            .OrderBy(eventItem => eventItem.StartAtUtc)
            .Take(3000)
            .ToListAsync(cancellationToken);

        var corpus = active.Concat(pending).DistinctBy(eventItem => eventItem.Id).ToList();
        var model = TfIdfModel.Build(corpus
            .Select(eventItem => new ItemText(eventItem.Id, eventItem.Title, eventItem.Description, eventItem.Tags, eventItem.Category, eventItem.Locality))
            .ToList());
        var sameDay = active
            .Select(ToDuplicateCandidate)
            .GroupBy(candidate => candidate.LocalStartDate)
            .ToDictionary(group => group.Key, group => group.ToList());
        return (model, sameDay);
    }

    private async Task ReleaseClaimAsync(DateOnly today, DateOnly? previous) =>
        await dbContext.AutoModerationSettings
            .Where(row => row.Id == AutoModerationSettings.SingletonId && row.LastScheduledRunDate == today)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.LastScheduledRunDate, previous), CancellationToken.None);

    private static ModerationCandidate ToCandidate(Event eventItem) => new(
        eventItem.Id, eventItem.Revision, eventItem.OwnerId, eventItem.Title, eventItem.Description, eventItem.Tags,
        eventItem.VenueName, eventItem.OrganizerName, (double)eventItem.Latitude, (double)eventItem.Longitude,
        eventItem.StartAtUtc, eventItem.EndAtUtc, eventItem.Price);

    private static DuplicateCandidate ToDuplicateCandidate(Event eventItem) => new(
        eventItem.Id, eventItem.Title, (double)eventItem.Latitude, (double)eventItem.Longitude,
        AutoModerationSchedule.LocalDate(eventItem.StartAtUtc));
}

public sealed class AutoModerationOffException : Exception;
