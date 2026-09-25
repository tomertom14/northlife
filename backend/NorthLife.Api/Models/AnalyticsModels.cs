namespace NorthLife.Api.Models;

/// <summary>Interaction counts for one event in one UTC hour; the spike detector reads these.</summary>
public sealed class EventStatsHourly
{
    public Guid EventId { get; set; }
    public DateTimeOffset HourUtc { get; set; }
    public int Impressions { get; set; }
    public int DetailViews { get; set; }
    public int Navigations { get; set; }
    public int Shares { get; set; }
}

/// <summary>Counts for one event on one Jerusalem calendar day, plus a HyperLogLog of its visitors.</summary>
public sealed class EventStatsDaily
{
    public Guid EventId { get; set; }
    public DateOnly Day { get; set; }
    public int Impressions { get; set; }
    public int DetailViews { get; set; }
    public int Navigations { get; set; }
    public int Shares { get; set; }

    /// <summary>Estimated distinct visitors that day, from <see cref="VisitorSketch"/>.</summary>
    public int Visitors { get; set; }

    /// <summary>Serialized HyperLogLog; unions across days and events give unique visitors for any range.</summary>
    public byte[] VisitorSketch { get; set; } = [];
}

/// <summary>Forward-decayed popularity: log Σ wᵢ · e^(λ(tᵢ − L)). Higher means more popular right now.</summary>
public sealed class EventPopularity
{
    public Guid EventId { get; set; }
    public double LogScore { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>How far a background job has processed its input; moved in the same transaction as its output.</summary>
public sealed class AnalyticsCheckpoint
{
    public required string Name { get; set; }
    public DateTimeOffset ProcessedUntilUtc { get; set; }
}
