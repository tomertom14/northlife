using Prometheus;

namespace NorthLife.Api.Analytics;

/// <summary>
/// Operational metrics for Prometheus. Labels stay low-cardinality (interaction type, outcome):
/// per-event numbers belong in PostgreSQL, not in the time-series database.
/// </summary>
public sealed class AnalyticsMetrics
{
    private static readonly Counter Received = Metrics.CreateCounter(
        "northlife_analytics_interactions_received_total",
        "Interactions sent by browsers, before validation and deduplication.",
        new CounterConfiguration { LabelNames = ["type"] });

    private static readonly Counter Recorded = Metrics.CreateCounter(
        "northlife_analytics_interactions_recorded_total",
        "Interactions stored after the 30-minute deduplication window.");

    private static readonly Counter RollupRuns = Metrics.CreateCounter(
        "northlife_analytics_rollup_runs_total",
        "Rollup worker runs by outcome.",
        new CounterConfiguration { LabelNames = ["outcome"] });

    private static readonly Histogram RollupDuration = Metrics.CreateHistogram(
        "northlife_analytics_rollup_duration_seconds",
        "Time to roll one window of raw interactions into hourly and daily statistics.",
        new HistogramConfiguration { Buckets = Histogram.ExponentialBuckets(0.005, 2, 12) });

    private static readonly Gauge RollupLag = Metrics.CreateGauge(
        "northlife_analytics_rollup_lag_seconds",
        "How far the rollup checkpoint trails the database clock.");

    public void CountReceived(InteractionType type) => Received.WithLabels(type.ToString()).Inc();

    public void CountRecorded(int count) => Recorded.Inc(count);

    public void CountRollup(string outcome) => RollupRuns.WithLabels(outcome).Inc();

    public IDisposable TimeRollup() => RollupDuration.NewTimer();

    public void SetRollupLag(TimeSpan lag) => RollupLag.Set(Math.Max(0, lag.TotalSeconds));
}
