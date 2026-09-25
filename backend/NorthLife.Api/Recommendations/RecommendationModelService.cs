using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NorthLife.Api.Data;
using NorthLife.Api.Models;

namespace NorthLife.Api.Recommendations;

/// <summary>The latest content model, kept in memory for the diversity step of recommendations.</summary>
public sealed class RecommendationModelCache
{
    private volatile TfIdfModel? _content;

    public TfIdfModel? Content => _content;

    public DateTimeOffset? BuiltAtUtc { get; private set; }

    public void Publish(TfIdfModel content, DateTimeOffset builtAt)
    {
        _content = content;
        BuiltAtUtc = builtAt;
    }

    public double Similarity(Guid a, Guid b) => _content?.Cosine(a, b) ?? 0;
}

/// <summary>
/// Rebuilds the similarity model: TF-IDF over the text of upcoming events and of events visitors
/// engaged with in the last 30 days, item-item collaborative filtering over those 30 days, and the
/// blended top-20 neighbours of every such event among the upcoming ones, stored in
/// <c>event_similarities</c>. The worker runs it every 15 minutes, so new events get text-based
/// neighbours within minutes and behaviour takes over as visitors engage with them.
/// </summary>
public sealed class RecommendationModelService(
    AppDbContext dbContext,
    RecommendationModelCache cache,
    IOptions<RecommendationOptions> options,
    TimeProvider timeProvider)
{
    public const int HistoryDays = 30;

    /// <summary>Pairwise scoring is quadratic; beyond this many upcoming events only the soonest are modelled.</summary>
    public const int MaxTargets = 3000;

    public async Task<int> RefreshAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var since = now.AddDays(-HistoryDays);

        var targets = await dbContext.Events.AsNoTracking()
            .Where(item => item.Status == EventStatus.Published && item.DeletedAtUtc == null && item.EndAtUtc > now && item.Owner.SuspendedAtUtc == null)
            .OrderBy(item => item.StartAtUtc)
            .Take(MaxTargets)
            .Select(item => new ItemText(item.Id, item.Title, item.Description, item.Tags, item.Category, item.Locality))
            .ToListAsync(cancellationToken);

        var engagement = await dbContext.Database.SqlQuery<EngagementRow>($"""
            SELECT visitor_id AS "VisitorId", event_id AS "EventId",
                   sum(CASE type WHEN 2 THEN 3 WHEN 3 THEN 5 WHEN 4 THEN 4 ELSE 0 END)::double precision AS "Weight"
            FROM interactions
            WHERE occurred_at_utc > {since} AND type IN (2, 3, 4)
            GROUP BY visitor_id, event_id
            """).ToListAsync(cancellationToken);

        var targetIds = targets.Select(item => item.Id).ToHashSet();
        var engagedIds = engagement.Select(row => row.EventId).Distinct().Where(id => !targetIds.Contains(id)).ToList();
        var history = await dbContext.Events.AsNoTracking()
            .Where(item => engagedIds.Contains(item.Id) && item.DeletedAtUtc == null)
            .Select(item => new ItemText(item.Id, item.Title, item.Description, item.Tags, item.Category, item.Locality))
            .ToListAsync(cancellationToken);

        var corpus = targets.Concat(history).ToList();
        var content = TfIdfModel.Build(corpus);
        var visitorItems = engagement
            .GroupBy(row => row.VisitorId)
            .ToDictionary(group => group.Key, group => group.ToDictionary(row => row.EventId, row => row.Weight));
        var collaborative = Collaborative.ItemSimilarities(visitorItems);
        var neighbours = SimilarityBlender.TopNeighbours(corpus.Select(item => item.Id), targetIds, content, collaborative, options.Value);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.EventSimilarities.ExecuteDeleteAsync(cancellationToken);
        var rows = neighbours.SelectMany(pair => pair.Value.Select((neighbour, index) => new EventSimilarity
        {
            SourceEventId = pair.Key,
            TargetEventId = neighbour.Target,
            Rank = (short)(index + 1),
            Content = neighbour.Content,
            Collaborative = neighbour.Collaborative,
            Blended = neighbour.Blended,
            CoVisitors = neighbour.CoVisitors,
            ComputedAtUtc = now,
        })).ToList();
        dbContext.EventSimilarities.AddRange(rows);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();

        cache.Publish(content, now);
        return rows.Count;
    }

    private sealed class EngagementRow
    {
        public Guid VisitorId { get; set; }
        public Guid EventId { get; set; }
        public double Weight { get; set; }
    }
}
