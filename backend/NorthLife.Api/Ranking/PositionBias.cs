namespace NorthLife.Api.Ranking;

/// <summary>Impressions and clicks of one event at one feed position in one list (context).</summary>
public sealed record PositionCell(Guid EventId, int Position, int Impressions, int Clicks, int Context = 0);

/// <param name="Propensity">θ_k / θ_1 after isotonic smoothing (never increasing with position).</param>
/// <param name="RawPropensity">θ_k / θ_1 straight from expectation-maximisation.</param>
public sealed record PositionPropensity(int Position, double Propensity, double RawPropensity, double NaiveRatio, int Impressions, int Clicks);

/// <summary>
/// Estimates how much attention each feed position gets, so clicks can be credited fairly: an
/// event at the top is clicked more partly because it is at the top.
/// <para>
/// Model: the position-based click model (Craswell et al., 2008; Chuklin, Markov and de Rijke,
/// 2015), P(click | event e at position k) = θ_k · α_e, where θ_k is the chance the visitor examines
/// position k and α_e the chance they open e once they notice it. Both are hidden, so they are fitted
/// by expectation–maximisation over the logged (event, position) counts:
/// </para>
/// <code>
/// θ_k ← Σ_e [c + (n − c) · θ_k(1 − α_e) / (1 − θ_k α_e)] / Σ_e n
/// α_e ← Σ_k [c + (n − c) · α_e(1 − θ_k) / (1 − θ_k α_e)] / Σ_k n
/// </code>
/// <para>
/// where n and c are the impressions and clicks of the cell. Appeal is fitted per list and event
/// (α_{q,e}, as per query and document in search), because lists have different audiences: fans
/// read a category list, so the same event draws more clicks there whatever its position. What
/// identifies θ is the same event shown at different positions within one list, which the
/// randomised top-N shuffle of the hot feed provides. The naive click-through ratio per position
/// cannot separate position from the appeal of whatever sits there. Only θ_k / θ_1 is reported.
/// Each iteration is O(cells).
/// </para>
/// </summary>
public static class PositionBiasEstimator
{
    public static IReadOnlyList<PositionPropensity> Estimate(
        IReadOnlyList<PositionCell> cells,
        int maxPosition = 12,
        int maxIterations = 500,
        double tolerance = 1e-8)
    {
        var usable = cells
            .Where(cell => cell.Position >= 1 && cell.Position <= maxPosition && cell.Impressions > 0)
            .Select(cell => cell with { Clicks = Math.Min(cell.Clicks, cell.Impressions) })
            .ToList();
        if (usable.Count == 0) return [];

        var positions = usable.Select(cell => cell.Position).Distinct().Order().ToList();
        var theta = positions.ToDictionary(position => position, _ => 0.5);
        var alpha = usable
            .GroupBy(cell => (cell.Context, cell.EventId))
            .ToDictionary(
                group => group.Key,
                group => Math.Clamp((group.Sum(cell => cell.Clicks) + 0.5) / (group.Sum(cell => cell.Impressions) + 1.0), 0.001, 0.999));

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            var thetaNumerator = positions.ToDictionary(position => position, _ => 0.0);
            var thetaDenominator = positions.ToDictionary(position => position, _ => 0.0);
            var alphaNumerator = alpha.Keys.ToDictionary(id => id, _ => 0.0);
            var alphaDenominator = alpha.Keys.ToDictionary(id => id, _ => 0.0);

            foreach (var cell in usable)
            {
                var key = (cell.Context, cell.EventId);
                var t = theta[cell.Position];
                var a = alpha[key];
                var skipped = cell.Impressions - cell.Clicks;
                var unclicked = 1 - t * a;
                thetaNumerator[cell.Position] += cell.Clicks + skipped * t * (1 - a) / unclicked;
                thetaDenominator[cell.Position] += cell.Impressions;
                alphaNumerator[key] += cell.Clicks + skipped * a * (1 - t) / unclicked;
                alphaDenominator[key] += cell.Impressions;
            }

            var change = 0.0;
            foreach (var position in positions)
            {
                var next = Math.Clamp(thetaNumerator[position] / thetaDenominator[position], 1e-6, 1 - 1e-6);
                change = Math.Max(change, Math.Abs(next - theta[position]));
                theta[position] = next;
            }

            foreach (var id in alpha.Keys.ToList())
            {
                var next = Math.Clamp(alphaNumerator[id] / alphaDenominator[id], 1e-6, 1 - 1e-6);
                change = Math.Max(change, Math.Abs(next - alpha[id]));
                alpha[id] = next;
            }

            if (change < tolerance) break;
        }

        var first = positions[0];
        var byPosition = usable.GroupBy(cell => cell.Position).ToDictionary(
            group => group.Key,
            group => (Impressions: group.Sum(cell => cell.Impressions), Clicks: group.Sum(cell => cell.Clicks)));
        var firstRate = Rate(byPosition[first]);
        var raw = positions.Select(position => theta[position] / theta[first]).ToArray();
        var smoothed = IsotonicRegression.NonIncreasing(raw, positions.Select(position => (double)byPosition[position].Impressions).ToArray());
        var scale = smoothed[0] > 0 ? smoothed[0] : 1;
        return positions.Select((position, index) => new PositionPropensity(
            position,
            smoothed[index] / scale,
            raw[index],
            firstRate > 0 ? Rate(byPosition[position]) / firstRate : 0,
            byPosition[position].Impressions,
            byPosition[position].Clicks)).ToList();
    }

    private static double Rate((int Impressions, int Clicks) counts) => counts.Impressions == 0 ? 0 : (double)counts.Clicks / counts.Impressions;
}

/// <summary>
/// Weighted isotonic regression by the Pool-Adjacent-Violators algorithm (Ayer et al., 1955): the
/// closest non-increasing sequence in weighted least squares. Attention cannot grow further down
/// the page, so sampling noise that breaks that order (a lower position estimated above a higher
/// one from a few dozen clicks) is pooled away. Weights are impressions, so well-measured positions
/// dominate. O(n).
/// </summary>
public static class IsotonicRegression
{
    public static double[] NonIncreasing(IReadOnlyList<double> values, IReadOnlyList<double> weights)
    {
        var blocks = new List<(double Mean, double Weight, int Count)>();
        for (var index = 0; index < values.Count; index++)
        {
            var block = (Mean: values[index], Weight: Math.Max(weights[index], 1e-9), Count: 1);
            // Merge while the new block is higher than the one before it (a violation).
            while (blocks.Count > 0 && blocks[^1].Mean < block.Mean)
            {
                var previous = blocks[^1];
                blocks.RemoveAt(blocks.Count - 1);
                var weight = previous.Weight + block.Weight;
                block = ((previous.Mean * previous.Weight + block.Mean * block.Weight) / weight, weight, previous.Count + block.Count);
            }

            blocks.Add(block);
        }

        return blocks.SelectMany(block => Enumerable.Repeat(block.Mean, block.Count)).ToArray();
    }
}

/// <summary>Examination propensities per feed position, used to weight clicks by inverse propensity.</summary>
public sealed class PropensityTable(IReadOnlyDictionary<int, double> propensities)
{
    /// <summary>Clip for inverse-propensity weights: bounds the variance a rarely seen position can add.</summary>
    public const double MaxWeight = 5;

    public static PropensityTable Uniform { get; } = new(new Dictionary<int, double>());

    public IReadOnlyDictionary<int, double> Propensities { get; } = propensities;

    /// <summary>1 / θ_k, clipped to [1, <see cref="MaxWeight"/>]; positions without an estimate count as 1.</summary>
    public double InverseWeight(int? position) =>
        position is { } k && Propensities.TryGetValue(k, out var theta) && theta > 0
            ? Math.Clamp(1 / theta, 1, MaxWeight)
            : 1;
}
