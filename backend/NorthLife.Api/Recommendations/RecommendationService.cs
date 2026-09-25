using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NorthLife.Api.Analytics;
using NorthLife.Api.Contracts;
using NorthLife.Api.Data;
using NorthLife.Api.Models;
using NorthLife.Api.Services;

namespace NorthLife.Api.Recommendations;

public sealed record RecommendationItem(EventSummaryResponse Event, string Reason, Guid? BecauseOfEventId, string? BecauseOfTitle);

public sealed record RecommendationsResponse(bool Personalised, IReadOnlyList<RecommendationItem> Items);

/// <summary>"For you" and "more like this", built on the stored neighbours and the visitor's own history.</summary>
public sealed class RecommendationService(
    AppDbContext dbContext,
    PublicEventQueryService events,
    RecommendationModelCache cache,
    IOptions<RecommendationOptions> options,
    TimeProvider timeProvider)
{
    public const int MaxLimit = 20;

    public async Task<RecommendationsResponse> ForYouAsync(Guid? visitorId, PublicEventQueryParameters? filters, int limit, CancellationToken cancellationToken)
    {
        limit = Math.Clamp(limit, 1, MaxLimit);
        var now = timeProvider.GetUtcNow();
        var eligible = (await events.EligibleIdsAsync(filters, cancellationToken)).ToHashSet();
        if (eligible.Count == 0) return new RecommendationsResponse(false, []);

        var history = new List<RawInteraction>();
        if (visitorId is { } visitor && visitor != Guid.Empty)
        {
            var since = now.AddDays(-RecommendationModelService.HistoryDays);
            history = (await dbContext.Database.SqlQuery<HistoryRow>($"""
                    SELECT event_id AS "EventId", type AS "Type", occurred_at_utc AS "OccurredAtUtc"
                    FROM interactions
                    WHERE visitor_id = {visitor} AND occurred_at_utc > {since} AND type IN (2, 3, 4)
                    """).ToListAsync(cancellationToken))
                .Select(row => new RawInteraction(row.EventId, visitor, (InteractionType)row.Type, InteractionSource.Direct, null, row.OccurredAtUtc))
                .ToList();
        }

        var profile = Recommender.Profile(history, now, options.Value);
        var seen = history.Where(row => row.Type == InteractionType.DetailView).Select(row => row.EventId).ToHashSet();
        var sources = profile.Keys.ToList();
        var neighbours = (await dbContext.EventSimilarities.AsNoTracking()
                .Where(row => sources.Contains(row.SourceEventId))
                .ToListAsync(cancellationToken))
            .GroupBy(row => row.SourceEventId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<Neighbour>)group.OrderBy(row => row.Rank)
                    .Select(row => new Neighbour(row.TargetEventId, row.Content, row.Collaborative, row.Blended, row.CoVisitors))
                    .ToList());

        var eligibleList = eligible.ToList();
        var popularityLogs = await dbContext.EventPopularity.AsNoTracking()
            .Where(row => eligibleList.Contains(row.EventId))
            .ToDictionaryAsync(row => row.EventId, row => row.LogScore, cancellationToken);
        var popularity = Recommender.ScalePopularity(popularityLogs.ToDictionary(pair => pair.Key, pair => DecayedPopularity.ScoreAt(pair.Value, now)));

        var picks = Recommender.Recommend(
            profile,
            id => neighbours.TryGetValue(id, out var list) ? list : [],
            eligible,
            seen,
            popularity,
            cache.Similarity,
            limit,
            options.Value);

        var summaries = await events.SummariesAsync(picks.Select(pick => pick.EventId).ToList(), cancellationToken);
        var becauseIds = picks.Where(pick => pick.BecauseOf is not null).Select(pick => pick.BecauseOf!.Value).Distinct().ToList();
        var titles = await dbContext.Events.AsNoTracking()
            .Where(item => becauseIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Title, cancellationToken);

        var items = picks
            .Where(pick => summaries.ContainsKey(pick.EventId))
            .Select(pick => new RecommendationItem(
                summaries[pick.EventId],
                pick.Kind,
                pick.BecauseOf,
                pick.BecauseOf is { } because && titles.TryGetValue(because, out var title) ? title : null))
            .ToList();
        return new RecommendationsResponse(profile.Count > 0, items);
    }

    /// <summary>The event's stored neighbours that are still upcoming; the same category as a fallback.</summary>
    public async Task<IReadOnlyList<EventSummaryResponse>> SimilarAsync(Guid eventId, int limit, CancellationToken cancellationToken)
    {
        limit = Math.Clamp(limit, 1, MaxLimit);
        var eligible = (await events.EligibleIdsAsync(null, cancellationToken)).ToHashSet();
        eligible.Remove(eventId);
        var neighbours = await dbContext.EventSimilarities.AsNoTracking()
            .Where(row => row.SourceEventId == eventId)
            .OrderBy(row => row.Rank)
            .Select(row => row.TargetEventId)
            .ToListAsync(cancellationToken);
        var ids = neighbours.Where(eligible.Contains).Take(limit).ToList();

        if (ids.Count < limit)
        {
            var category = await dbContext.Events.AsNoTracking().Where(item => item.Id == eventId).Select(item => (EventCategory?)item.Category).SingleOrDefaultAsync(cancellationToken);
            if (category is not null)
            {
                var fallback = await dbContext.Events.AsNoTracking()
                    .Where(item => item.Category == category && eligible.Contains(item.Id) && !ids.Contains(item.Id))
                    .OrderBy(item => item.StartAtUtc)
                    .Select(item => item.Id)
                    .Take(limit - ids.Count)
                    .ToListAsync(cancellationToken);
                ids.AddRange(fallback);
            }
        }

        var summaries = await events.SummariesAsync(ids, cancellationToken);
        return ids.Where(summaries.ContainsKey).Select(id => summaries[id]).ToList();
    }

    private sealed class HistoryRow
    {
        public Guid EventId { get; set; }
        public short Type { get; set; }
        public DateTimeOffset OccurredAtUtc { get; set; }
    }
}
