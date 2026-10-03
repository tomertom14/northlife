namespace NorthLife.Api.Analytics;

public sealed class RollupBatch
{
    public Dictionary<(Guid EventId, DateTimeOffset HourUtc), InteractionCounts> Hourly { get; } = [];
    public Dictionary<(Guid EventId, DateOnly Day), InteractionCounts> Daily { get; } = [];
    public Dictionary<(Guid EventId, DateOnly Day), HashSet<Guid>> Visitors { get; } = [];

    /// <summary>Per event, log Σ wᵢ · e^(λ(tᵢ − L)) of this batch (see <see cref="DecayedPopularity"/>).</summary>
    public Dictionary<Guid, double> PopularityLog { get; } = [];

    public int RowCount { get; set; }
}

/// <summary>
/// Turns one window of raw interactions into additive rollup deltas in a single pass. Pure, so the
/// counting rules are unit-tested without a database; the worker then applies the deltas and moves
/// its checkpoint in one transaction, which makes every raw row count exactly once.
/// </summary>
public static class RollupAggregator
{
    public static RollupBatch Aggregate(IEnumerable<RawInteraction> rows, Func<RawInteraction, double> weight)
    {
        var batch = new RollupBatch();
        foreach (var row in rows)
        {
            batch.RowCount++;
            var hour = new DateTimeOffset(row.OccurredAtUtc.UtcDateTime.Date.AddHours(row.OccurredAtUtc.UtcDateTime.Hour), TimeSpan.Zero);
            var day = JerusalemDays.Of(row.OccurredAtUtc);

            Increment(batch.Hourly, (row.EventId, hour), row.Type);
            Increment(batch.Daily, (row.EventId, day), row.Type);

            if (!batch.Visitors.TryGetValue((row.EventId, day), out var visitors))
            {
                visitors = [];
                batch.Visitors[(row.EventId, day)] = visitors;
            }

            visitors.Add(row.VisitorId);

            var w = weight(row);
            if (w > 0)
            {
                var contribution = DecayedPopularity.LogContribution(w, row.OccurredAtUtc);
                batch.PopularityLog[row.EventId] = batch.PopularityLog.TryGetValue(row.EventId, out var current)
                    ? DecayedPopularity.LogAddExp(current, contribution)
                    : contribution;
            }
        }

        return batch;
    }

    private static void Increment<TKey>(Dictionary<TKey, InteractionCounts> counts, TKey key, InteractionType type)
        where TKey : notnull
    {
        counts.TryGetValue(key, out var value);
        value.Add(type);
        counts[key] = value;
    }
}
