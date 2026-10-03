using Microsoft.EntityFrameworkCore;
using NorthLife.Api.Analytics;
using NorthLife.Api.Data;
using NorthLife.Api.Models;

namespace NorthLife.Api.Ranking;

/// <summary>Fits the feed's position bias from recent traffic and serves it to the rollup.</summary>
public sealed class PositionBiasService(AppDbContext dbContext, TimeProvider timeProvider)
{
    public const int MaxPosition = 12;
    public const int WindowDays = 30;

    /// <summary>Re-estimates θ_k from the last 30 days of feed impressions and clicks and stores it.</summary>
    public async Task<IReadOnlyList<PositionPropensity>> EstimateAsync(CancellationToken cancellationToken)
    {
        var cells = await dbContext.Database.SqlQuery<CellRow>($"""
            SELECT event_id AS "EventId", position::int AS "Position", coalesce(context_key, 0) AS "Context",
                   count(*) FILTER (WHERE type = 1)::int AS "Impressions",
                   count(*) FILTER (WHERE type = 2)::int AS "Clicks"
            FROM interactions
            WHERE source = 1 AND position BETWEEN 1 AND {MaxPosition}
              AND occurred_at_utc > now() - make_interval(days => {WindowDays})
            GROUP BY event_id, position, coalesce(context_key, 0)
            """).ToListAsync(cancellationToken);

        var estimate = PositionBiasEstimator.Estimate(
            cells.Select(cell => new PositionCell(cell.EventId, cell.Position, cell.Impressions, cell.Clicks, cell.Context)).ToList(),
            MaxPosition);
        await StoreAsync(estimate, cancellationToken);
        return estimate;
    }

    public async Task StoreAsync(IReadOnlyList<PositionPropensity> estimate, CancellationToken cancellationToken)
    {
        if (estimate.Count == 0) return;
        var now = timeProvider.GetUtcNow();
        await dbContext.PositionPropensities.Where(row => row.Surface == (short)InteractionSource.Feed).ExecuteDeleteAsync(cancellationToken);
        dbContext.PositionPropensities.AddRange(estimate.Select(row => new PositionPropensityRow
        {
            Surface = (short)InteractionSource.Feed,
            Position = (short)row.Position,
            Propensity = row.Propensity,
            RawPropensity = row.RawPropensity,
            NaiveRatio = row.NaiveRatio,
            Impressions = row.Impressions,
            Clicks = row.Clicks,
            EstimatedAtUtc = now,
        }));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<PropensityTable> LoadAsync(CancellationToken cancellationToken)
    {
        var rows = await dbContext.PositionPropensities.AsNoTracking()
            .Where(row => row.Surface == (short)InteractionSource.Feed)
            .ToListAsync(cancellationToken);
        return rows.Count == 0 ? PropensityTable.Uniform : new PropensityTable(rows.ToDictionary(row => (int)row.Position, row => row.Propensity));
    }

    public Task<List<PositionPropensityRow>> CurrentAsync(CancellationToken cancellationToken) =>
        dbContext.PositionPropensities.AsNoTracking()
            .Where(row => row.Surface == (short)InteractionSource.Feed)
            .OrderBy(row => row.Position)
            .ToListAsync(cancellationToken);

    private sealed class CellRow
    {
        public Guid EventId { get; set; }
        public int Position { get; set; }
        public int Context { get; set; }
        public int Impressions { get; set; }
        public int Clicks { get; set; }
    }
}

/// <summary>
/// Weights of interactions in the popularity score used for ranking. Impressions add nothing:
/// being shown is not interest, and with a "hot" sort it would feed back into itself (shown more,
/// so scored higher, so shown more). A feed click counts 3 / θ_k, the inverse-propensity weight of
/// its position (clipped at 5), so a click on a card far down the page is worth more than one at
/// the top that got there by being first.
/// </summary>
public static class PopularityWeights
{
    public static Func<RawInteraction, double> With(PropensityTable propensities) => row => row.Type switch
    {
        InteractionType.DetailView when row.Source == InteractionSource.Feed => 3 * propensities.InverseWeight(row.Position),
        InteractionType.DetailView => 3,
        InteractionType.Navigate => 5,
        InteractionType.Share => 4,
        _ => 0,
    };
}
