namespace NorthLife.Api.Analytics;

public sealed record SpikeResult(double ZScore, double Baseline, int Observed, bool IsSpike);

/// <summary>
/// Flags an hour whose count is far above the event's recent level.
/// <para>
/// The baseline is an exponentially weighted moving average (EWMA) of the earlier hours with
/// α = 2 / (N + 1) for N = 24, so roughly the last day matters most. The spread is the matching
/// exponentially weighted variance (West, 1979):
/// μ ← μ + αδ and σ² ← (1 − α)(σ² + αδ²), where δ = x − μ.
/// The last hour is scored as z = (x − μ) / σ and counts as a spike when z ≥ 3 and the hour has at
/// least ten interactions, so a jump from 0 to 2 views is not news. Counts behave roughly like a
/// Poisson process, whose variance equals its mean, so σ² is never allowed below max(μ, 1).
/// </para>
/// <para>
/// A steady ramp keeps z low because the average follows it; only a sudden break from the recent
/// pattern scores high. The whole evaluation is O(n) over the hourly series.
/// </para>
/// </summary>
public static class SpikeDetector
{
    public const double DefaultAlpha = 2.0 / (24 + 1);
    public const double DefaultThreshold = 3.0;
    public const int DefaultMinimumCount = 10;

    /// <param name="hourly">Counts per hour, oldest first, including zero hours; the last item is scored.</param>
    public static SpikeResult Evaluate(
        IReadOnlyList<int> hourly,
        double alpha = DefaultAlpha,
        double threshold = DefaultThreshold,
        int minimumCount = DefaultMinimumCount)
    {
        if (hourly.Count == 0) return new SpikeResult(0, 0, 0, false);
        var observed = hourly[^1];
        if (hourly.Count == 1) return new SpikeResult(0, 0, observed, false);

        double mean = hourly[0];
        var variance = 0.0;
        for (var index = 1; index < hourly.Count - 1; index++)
        {
            var delta = hourly[index] - mean;
            mean += alpha * delta;
            variance = (1 - alpha) * (variance + alpha * delta * delta);
        }

        var spread = Math.Sqrt(Math.Max(variance, Math.Max(mean, 1)));
        var z = (observed - mean) / spread;
        return new SpikeResult(z, mean, observed, z >= threshold && observed >= minimumCount);
    }
}
