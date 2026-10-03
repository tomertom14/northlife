using Microsoft.EntityFrameworkCore;
using NorthLife.Api.Data;
using NorthLife.Api.Models;

namespace NorthLife.Api.Analytics;

public sealed class AnalyticsOptions
{
    public const string SectionName = "Analytics";

    public bool WorkerEnabled { get; set; } = true;

    /// <summary>How often the worker rolls new interactions up.</summary>
    public int RollupIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Rows younger than this are left for the next run. Interactions take their time from the
    /// database clock when they are inserted, and an insert commits in milliseconds, so every row
    /// older than the lag is visible and none can appear later behind the checkpoint.
    /// </summary>
    public int IngestLagSeconds { get; set; } = 120;

    /// <summary>Raw interactions are kept this long; monthly partitions older than that are dropped.</summary>
    public int RawRetentionDays { get; set; } = 90;
}

/// <summary>
/// Rolls raw interactions into hourly and daily statistics, visitor sketches and decayed popularity.
/// Each run takes the window between the checkpoint and (database now − lag), aggregates it in one
/// pass, applies the additive deltas and moves the checkpoint in the same transaction: a crash rolls
/// everything back, so every raw row is counted exactly once.
/// </summary>
public sealed class AnalyticsRollupService(AppDbContext dbContext, AnalyticsMetrics metrics)
{
    public const string CheckpointName = "rollup";

    /// <summary>After downtime, catch up in windows of at most this size to bound memory.</summary>
    public static readonly TimeSpan MaxWindow = TimeSpan.FromHours(6);

    /// <summary>Advisory lock held by whatever changes the rolled-up statistics: the worker and the demo seeder.</summary>
    internal const long RollupLockKey = 7_261_300;

    /// <summary>
    /// Weight of one interaction in the popularity score. The worker sets it from the current
    /// position-bias estimate (<see cref="Ranking.PopularityWeights"/>) before each run.
    /// </summary>
    public Func<RawInteraction, double> Weight { get; set; } = Ranking.PopularityWeights.With(Ranking.PropensityTable.Uniform);

    /// <summary>
    /// Processes everything up to (database now − lag). Only a capped window means there is more
    /// backlog; once a window reaches "now − lag" the run stops. (Looping until the window is empty
    /// never ends: the clock moves on during each iteration, so there is always a sliver left.)
    /// </summary>
    public async Task<int> RunAsync(TimeSpan lag, CancellationToken cancellationToken)
    {
        var total = 0;
        while (await RunWindowAsync(lag, cancellationToken) is { } window)
        {
            total += window.Rows;
            if (window.CaughtUp) break;
        }

        return total;
    }

    /// <returns>Rows processed and whether the window reached now − lag, or null when there was nothing to do.</returns>
    private async Task<(int Rows, bool CaughtUp)?> RunWindowAsync(TimeSpan lag, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        // With several instances only one rolls up at a time; the others skip this round.
        var locked = await dbContext.Database
            .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({RollupLockKey}) AS \"Value\"")
            .SingleAsync(cancellationToken);
        if (!locked) return null;

        var now = await dbContext.Database.SqlQuery<DateTimeOffset>($"SELECT now() AS \"Value\"").SingleAsync(cancellationToken);
        var checkpoint = await dbContext.AnalyticsCheckpoints.SingleAsync(candidate => candidate.Name == CheckpointName, cancellationToken);
        var start = checkpoint.ProcessedUntilUtc;
        var end = now - lag;
        metrics.SetRollupLag(now - start);
        if (end <= start) return null;
        var capped = end - start > MaxWindow;
        if (capped) end = start + MaxWindow;

        using var timer = metrics.TimeRollup();
        var rows = await dbContext.Database.SqlQuery<InteractionRow>($"""
            SELECT interactions.event_id AS "EventId", interactions.visitor_id AS "VisitorId",
                   interactions.type AS "Type", interactions.source AS "Source",
                   interactions.position AS "Position", interactions.occurred_at_utc AS "OccurredAtUtc",
                   interactions.context_key AS "ContextKey"
            FROM interactions
            JOIN events ON events.id = interactions.event_id
            WHERE interactions.occurred_at_utc >= {start} AND interactions.occurred_at_utc < {end}
            """).ToListAsync(cancellationToken);

        var batch = RollupAggregator.Aggregate(rows.Select(row => row.ToRaw()), Weight);
        await ApplyBatchAsync(batch, now, cancellationToken);
        checkpoint.ProcessedUntilUtc = end;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        metrics.SetRollupLag(now - end);
        return (batch.RowCount, !capped);
    }

    /// <summary>Adds one batch of deltas to the tracked statistics rows; the caller saves and commits.</summary>
    public async Task ApplyBatchAsync(RollupBatch batch, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (batch.RowCount == 0) return;

        var hourlyEvents = batch.Hourly.Keys.Select(key => key.EventId).Distinct().ToList();
        var hours = batch.Hourly.Keys.Select(key => key.HourUtc).Distinct().ToList();
        var hourly = await dbContext.EventStatsHourly
            .Where(stats => hourlyEvents.Contains(stats.EventId) && hours.Contains(stats.HourUtc))
            .ToDictionaryAsync(stats => (stats.EventId, stats.HourUtc), cancellationToken);
        foreach (var (key, counts) in batch.Hourly)
        {
            if (!hourly.TryGetValue(key, out var row))
            {
                row = new EventStatsHourly { EventId = key.EventId, HourUtc = key.HourUtc };
                dbContext.EventStatsHourly.Add(row);
            }

            row.Impressions += counts.Impressions;
            row.DetailViews += counts.DetailViews;
            row.Navigations += counts.Navigations;
            row.Shares += counts.Shares;
        }

        var dailyEvents = batch.Daily.Keys.Select(key => key.EventId).Distinct().ToList();
        var days = batch.Daily.Keys.Select(key => key.Day).Distinct().ToList();
        var daily = await dbContext.EventStatsDaily
            .Where(stats => dailyEvents.Contains(stats.EventId) && days.Contains(stats.Day))
            .ToDictionaryAsync(stats => (stats.EventId, stats.Day), cancellationToken);
        foreach (var (key, counts) in batch.Daily)
        {
            if (!daily.TryGetValue(key, out var row))
            {
                row = new EventStatsDaily { EventId = key.EventId, Day = key.Day };
                dbContext.EventStatsDaily.Add(row);
            }

            row.Impressions += counts.Impressions;
            row.DetailViews += counts.DetailViews;
            row.Navigations += counts.Navigations;
            row.Shares += counts.Shares;

            // Adding a visitor who is already in the sketch changes nothing, so re-adding is harmless.
            var sketch = row.VisitorSketch.Length > 0 ? HyperLogLog.Deserialize(row.VisitorSketch) : new HyperLogLog();
            foreach (var visitor in batch.Visitors[key]) sketch.Add(visitor);
            row.VisitorSketch = sketch.Serialize();
            row.Visitors = (int)sketch.Count();
        }

        var popularEvents = batch.PopularityLog.Keys.ToList();
        var popularity = await dbContext.EventPopularity
            .Where(score => popularEvents.Contains(score.EventId))
            .ToDictionaryAsync(score => score.EventId, cancellationToken);
        foreach (var (eventId, logDelta) in batch.PopularityLog)
        {
            if (popularity.TryGetValue(eventId, out var row))
            {
                row.LogScore = DecayedPopularity.LogAddExp(row.LogScore, logDelta);
            }
            else
            {
                row = new EventPopularity { EventId = eventId, LogScore = logDelta };
                dbContext.EventPopularity.Add(row);
            }

            row.UpdatedAtUtc = now;
        }
    }

    /// <summary>Creates monthly partitions ahead of time and drops those past the retention period.</summary>
    public async Task<(int Created, int Dropped)> MaintainPartitionsAsync(int retentionDays, CancellationToken cancellationToken)
    {
        var created = await dbContext.Database
            .SqlQuery<int>($"SELECT analytics_ensure_interaction_partitions(2) AS \"Value\"")
            .SingleAsync(cancellationToken);
        var dropped = await dbContext.Database
            .SqlQuery<int>($"SELECT analytics_drop_interaction_partitions(now() - make_interval(days => {retentionDays})) AS \"Value\"")
            .SingleAsync(cancellationToken);
        return (created, dropped);
    }

    private sealed class InteractionRow
    {
        public Guid EventId { get; set; }
        public Guid VisitorId { get; set; }
        public short Type { get; set; }
        public short Source { get; set; }
        public short? Position { get; set; }
        public DateTimeOffset OccurredAtUtc { get; set; }
        public int? ContextKey { get; set; }

        public RawInteraction ToRaw() =>
            new(EventId, VisitorId, (InteractionType)Type, (InteractionSource)Source, Position, OccurredAtUtc, ContextKey);
    }
}
