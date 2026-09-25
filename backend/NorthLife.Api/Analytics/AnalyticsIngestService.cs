using Microsoft.EntityFrameworkCore;
using NorthLife.Api.Data;

namespace NorthLife.Api.Analytics;

public sealed record TrackedInteraction(Guid EventId, InteractionType Type, InteractionSource Source, int? Position, string? Context = null);

/// <summary>
/// Stores interactions from the public site. Visitors are random ids the browser keeps; no account,
/// address or other personal data is stored.
/// </summary>
public sealed class AnalyticsIngestService(AppDbContext dbContext, AnalyticsMetrics metrics)
{
    public const int MaxBatchSize = 50;
    public const int MaxPosition = 1000;
    public static readonly TimeSpan DeduplicationWindow = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Records a batch for one visitor and returns how many interactions were stored. Interactions
    /// with unknown or unpublished events are ignored, and the same visitor, event and type count at
    /// most once per 30 minutes, so reloading a page or scrolling back does not inflate the numbers.
    /// Impressions are counted per surface: a card seen in the editors' picks and again in the feed is
    /// two exposures, and the position-bias model needs the feed one.
    /// </summary>
    public async Task<int> RecordAsync(Guid visitorId, IReadOnlyList<TrackedInteraction> interactions, CancellationToken cancellationToken)
    {
        foreach (var interaction in interactions) metrics.CountReceived(interaction.Type);

        // Inside one batch the first of each (event, type) wins, per surface for impressions.
        var batch = interactions.DistinctBy(interaction => (interaction.EventId, interaction.Type, SurfaceKey(interaction))).ToList();
        if (batch.Count == 0) return 0;

        var eventIds = batch.Select(interaction => interaction.EventId).ToArray();
        var types = batch.Select(interaction => (short)interaction.Type).ToArray();
        var sources = batch.Select(interaction => (short)interaction.Source).ToArray();
        var positions = batch.Select(interaction => (short)(interaction.Position is >= 1 and <= MaxPosition ? interaction.Position.Value : -1)).ToArray();
        // 0 stands for "no list"; FeedContext never returns 0 for a real one.
        var contexts = batch.Select(interaction => FeedContext.Key(interaction.Context) ?? 0).ToArray();
        var visitorKey = visitorId.ToString("N");
        var windowMinutes = (int)DeduplicationWindow.TotalMinutes;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        // Batches of one visitor run one at a time, so two tabs cannot both pass the window check.
        await dbContext.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({visitorKey}, 13))", cancellationToken);
        var recorded = await dbContext.Database.ExecuteSqlAsync($"""
            INSERT INTO interactions (event_id, visitor_id, type, source, position, occurred_at_utc, context_key)
            SELECT batch.event_id, {visitorId}, batch.type, batch.source, NULLIF(batch.position, -1), now(), NULLIF(batch.context_key, 0)
            FROM unnest({eventIds}, {types}, {sources}, {positions}, {contexts}) AS batch(event_id, type, source, position, context_key)
            JOIN events ON events.id = batch.event_id
                AND events.status = 'Published'
                AND events.deleted_at_utc IS NULL
            WHERE NOT EXISTS (
                SELECT 1 FROM interactions recent
                WHERE recent.visitor_id = {visitorId}
                  AND recent.event_id = batch.event_id
                  AND recent.type = batch.type
                  AND (batch.type <> 1 OR recent.source = batch.source)
                  AND recent.occurred_at_utc > now() - make_interval(mins => {windowMinutes}))
            """, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        metrics.CountRecorded(recorded);
        return recorded;
    }

    private static InteractionSource? SurfaceKey(TrackedInteraction interaction) =>
        interaction.Type == InteractionType.Impression ? interaction.Source : null;

    /// <summary>Deletes a visitor's raw history ("reset my history"). Anonymous aggregates stay.</summary>
    public Task<int> ForgetAsync(Guid visitorId, CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlAsync($"DELETE FROM interactions WHERE visitor_id = {visitorId}", cancellationToken);
}
