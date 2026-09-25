using NorthLife.Api.Analytics;
using NorthLife.Api.Models;

namespace NorthLife.Tests;

public sealed class HyperLogLogTests
{
    private static IEnumerable<Guid> RandomGuids(int count, int seed)
    {
        var random = new Random(seed);
        var bytes = new byte[16];
        for (var index = 0; index < count; index++)
        {
            random.NextBytes(bytes);
            yield return new Guid(bytes);
        }
    }

    private static HyperLogLog SketchOf(IEnumerable<Guid> items)
    {
        var sketch = new HyperLogLog();
        foreach (var item in items) sketch.Add(item);
        return sketch;
    }

    private static double RelativeError(HyperLogLog sketch, int actual) => Math.Abs(sketch.Estimate() - actual) / actual;

    [Fact]
    public void Precision_14_has_16384_registers_and_under_one_percent_standard_error()
    {
        var sketch = new HyperLogLog();
        Assert.Equal(16_384, sketch.RegisterCount);
        Assert.Equal(0.008125, sketch.StandardError, 6);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(1_000)]
    [InlineData(10_000)]
    public void Small_sets_use_linear_counting_and_are_nearly_exact(int count)
    {
        Assert.True(RelativeError(SketchOf(RandomGuids(count, count)), count) < 0.01);
    }

    [Theory]
    [InlineData(100_000)]
    [InlineData(1_000_000)]
    public void Large_sets_stay_within_three_standard_errors(int count)
    {
        // 3 × 0.81% ≈ 2.4%: a single estimate lands inside with 99.7% probability.
        Assert.True(RelativeError(SketchOf(RandomGuids(count, 7)), count) < 0.025);
    }

    [Fact]
    public void Observed_error_matches_the_theoretical_standard_error()
    {
        const int trials = 30;
        const int count = 100_000;
        var squared = 0.0;
        for (var trial = 0; trial < trials; trial++)
        {
            var sketch = SketchOf(RandomGuids(count, 1000 + trial));
            var error = (sketch.Estimate() - count) / count;
            squared += error * error;
        }

        var rootMeanSquare = Math.Sqrt(squared / trials);
        Assert.InRange(rootMeanSquare, 0.004, 0.013);
    }

    [Fact]
    public void Adding_the_same_items_again_changes_nothing()
    {
        var items = RandomGuids(5_000, 3).ToList();
        var sketch = SketchOf(items);
        var before = sketch.Serialize();
        foreach (var item in items) sketch.Add(item);
        Assert.Equal(before, sketch.Serialize());
    }

    [Fact]
    public void Union_counts_overlapping_sets_once_and_is_commutative_and_idempotent()
    {
        var items = RandomGuids(100_000, 11).ToList();
        var first = SketchOf(items.Take(60_000));
        var second = SketchOf(items.Skip(40_000));

        var union = HyperLogLog.Deserialize(first.Serialize());
        union.Merge(second);
        var reversed = HyperLogLog.Deserialize(second.Serialize());
        reversed.Merge(first);

        Assert.True(RelativeError(union, 100_000) < 0.025);
        Assert.Equal(union.Serialize(), reversed.Serialize());
        var twice = HyperLogLog.Deserialize(union.Serialize());
        twice.Merge(union);
        Assert.Equal(union.Serialize(), twice.Serialize());
    }

    [Fact]
    public void Small_sketches_serialize_sparsely_and_large_ones_densely()
    {
        var small = SketchOf(RandomGuids(100, 5));
        var large = SketchOf(RandomGuids(50_000, 5));

        var smallBytes = small.Serialize();
        var largeBytes = large.Serialize();

        Assert.True(smallBytes.Length < 400, $"sparse encoding took {smallBytes.Length} bytes");
        Assert.Equal(3 + 16_384, largeBytes.Length);
        Assert.Equal(small.Count(), HyperLogLog.Deserialize(smallBytes).Count());
        Assert.Equal(large.Count(), HyperLogLog.Deserialize(largeBytes).Count());
    }

    [Fact]
    public void Union_of_stored_sketches_skips_empty_values()
    {
        var union = HyperLogLog.Union([null, [], SketchOf(RandomGuids(500, 9)).Serialize()]);
        Assert.True(RelativeError(union, 500) < 0.01);
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 9, 14, 0 })]
    [InlineData(new byte[] { 1, 14, 1, 0 })]
    [InlineData(new byte[] { 1, 14, 0, 2, 0, 1 })]
    [InlineData(new byte[] { 1, 3, 0, 0, 0 })]
    public void Rejects_corrupt_data(byte[] data)
    {
        Assert.Throws<FormatException>(() => HyperLogLog.Deserialize(data));
    }

    [Fact]
    public void Rejects_merging_different_precisions()
    {
        Assert.Throws<ArgumentException>(() => new HyperLogLog(14).Merge(new HyperLogLog(12)));
    }
}

public sealed class DecayedPopularityTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_interaction_is_worth_half_after_one_half_life()
    {
        var log = DecayedPopularity.LogContribution(1, Start);
        Assert.Equal(1, DecayedPopularity.ScoreAt(log, Start), 9);
        Assert.Equal(0.5, DecayedPopularity.ScoreAt(log, Start.AddHours(6)), 9);
        Assert.Equal(0.25, DecayedPopularity.ScoreAt(log, Start.AddHours(12)), 9);
    }

    [Fact]
    public void Forward_decay_equals_the_directly_decayed_sum()
    {
        var random = new Random(21);
        var interactions = Enumerable.Range(0, 200)
            .Select(_ => (Weight: 1 + random.Next(5), At: Start.AddMinutes(random.Next(0, 72 * 60))))
            .ToList();
        var now = Start.AddDays(3);

        var log = double.NegativeInfinity;
        foreach (var (weight, at) in interactions) log = DecayedPopularity.LogAddExp(log, DecayedPopularity.LogContribution(weight, at));
        var direct = interactions.Sum(item => item.Weight * Math.Exp(-DecayedPopularity.Lambda * (now - item.At).TotalHours));

        Assert.Equal(direct, DecayedPopularity.ScoreAt(log, now), direct * 1e-9);
    }

    [Fact]
    public void Ordering_by_the_stored_value_matches_ordering_by_current_popularity()
    {
        // Many interactions two days ago against a few in the last hour.
        var old = Enumerable.Repeat(Start, 40).Aggregate(double.NegativeInfinity, (sum, at) => DecayedPopularity.LogAddExp(sum, DecayedPopularity.LogContribution(3, at)));
        var fresh = Enumerable.Repeat(Start.AddHours(47), 3).Aggregate(double.NegativeInfinity, (sum, at) => DecayedPopularity.LogAddExp(sum, DecayedPopularity.LogContribution(3, at)));

        foreach (var hoursLater in new[] { 48, 60, 100 })
        {
            var now = Start.AddHours(hoursLater);
            Assert.Equal(
                fresh > old,
                DecayedPopularity.ScoreAt(fresh, now) > DecayedPopularity.ScoreAt(old, now));
        }

        Assert.True(fresh > old);
    }

    [Fact]
    public void Log_space_does_not_overflow_years_after_the_landmark()
    {
        var farFuture = DecayedPopularity.Landmark.AddYears(5);
        var log = DecayedPopularity.LogAddExp(
            DecayedPopularity.LogContribution(5, farFuture),
            DecayedPopularity.LogContribution(5, farFuture));
        Assert.True(double.IsFinite(log));
        Assert.Equal(10, DecayedPopularity.ScoreAt(log, farFuture), 6);
    }

    [Fact]
    public void Log_add_exp_treats_negative_infinity_as_zero()
    {
        Assert.Equal(3, DecayedPopularity.LogAddExp(double.NegativeInfinity, 3));
        Assert.Equal(1000 + Math.Log(2), DecayedPopularity.LogAddExp(1000, 1000), 9);
    }
}

public sealed class SpikeDetectorTests
{
    private static int Poisson(Random random, double mean)
    {
        // Knuth's method; fine for small means.
        var limit = Math.Exp(-mean);
        var product = random.NextDouble();
        var count = 0;
        while (product > limit)
        {
            count++;
            product *= random.NextDouble();
        }

        return count;
    }

    [Fact]
    public void Ordinary_noise_is_not_a_spike()
    {
        var random = new Random(4);
        var series = Enumerable.Range(0, 168).Select(_ => Poisson(random, 5)).ToList();
        series.Add(7);
        var result = SpikeDetector.Evaluate(series);
        Assert.False(result.IsSpike);
        Assert.InRange(result.Baseline, 3.5, 6.5);
    }

    [Fact]
    public void A_sudden_jump_is_a_spike()
    {
        var random = new Random(5);
        var series = Enumerable.Range(0, 168).Select(_ => Poisson(random, 3)).ToList();
        series.Add(30);
        var result = SpikeDetector.Evaluate(series);
        Assert.True(result.IsSpike);
        Assert.True(result.ZScore > 5);
    }

    [Fact]
    public void A_steady_ramp_is_followed_not_flagged()
    {
        var series = Enumerable.Range(1, 60).ToList();
        Assert.False(SpikeDetector.Evaluate(series).IsSpike);
    }

    [Fact]
    public void Tiny_counts_never_qualify_even_with_a_high_score()
    {
        var series = Enumerable.Repeat(0, 48).Append(8).ToList();
        var result = SpikeDetector.Evaluate(series);
        Assert.True(result.ZScore >= 3);
        Assert.False(result.IsSpike);
    }

    [Fact]
    public void Short_series_are_not_scored()
    {
        Assert.False(SpikeDetector.Evaluate([]).IsSpike);
        Assert.False(SpikeDetector.Evaluate([50]).IsSpike);
    }
}

public sealed class RollupAggregatorTests
{
    private static readonly Guid EventA = Guid.NewGuid();
    private static readonly Guid EventB = Guid.NewGuid();

    [Fact]
    public void Counts_by_hour_and_by_jerusalem_day_with_distinct_visitors()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        // 21:30 UTC in September is 00:30 the next day in Israel (UTC+3).
        var lateEvening = new DateTimeOffset(2026, 9, 24, 21, 30, 0, TimeSpan.Zero);
        var rows = new[]
        {
            new RawInteraction(EventA, alice, InteractionType.Impression, InteractionSource.Feed, 1, lateEvening),
            new RawInteraction(EventA, alice, InteractionType.DetailView, InteractionSource.Feed, 1, lateEvening.AddMinutes(5)),
            new RawInteraction(EventA, bob, InteractionType.Impression, InteractionSource.Feed, 2, lateEvening.AddMinutes(40)),
            new RawInteraction(EventB, bob, InteractionType.Navigate, InteractionSource.Details, null, lateEvening.AddMinutes(41)),
        };

        var batch = RollupAggregator.Aggregate(rows, row => InteractionWeights.Base(row.Type));

        Assert.Equal(4, batch.RowCount);
        var ninePm = new DateTimeOffset(2026, 9, 24, 21, 0, 0, TimeSpan.Zero);
        var tenPm = ninePm.AddHours(1);
        Assert.Equal(1, batch.Hourly[(EventA, ninePm)].Impressions);
        Assert.Equal(1, batch.Hourly[(EventA, ninePm)].DetailViews);
        Assert.Equal(1, batch.Hourly[(EventA, tenPm)].Impressions);

        var israelDay = new DateOnly(2026, 9, 25);
        Assert.Equal(2, batch.Daily[(EventA, israelDay)].Impressions);
        Assert.Equal(2, batch.Visitors[(EventA, israelDay)].Count);
        Assert.Equal(1, batch.Daily[(EventB, israelDay)].Navigations);
    }

    [Fact]
    public void Popularity_delta_is_the_log_sum_of_weighted_contributions()
    {
        var at = new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);
        var rows = new[]
        {
            new RawInteraction(EventA, Guid.NewGuid(), InteractionType.DetailView, InteractionSource.Feed, 3, at),
            new RawInteraction(EventA, Guid.NewGuid(), InteractionType.Navigate, InteractionSource.Details, null, at),
        };

        var batch = RollupAggregator.Aggregate(rows, row => InteractionWeights.Base(row.Type));

        // Weights 3 and 5 at the same instant add up to 8.
        Assert.Equal(8, DecayedPopularity.ScoreAt(batch.PopularityLog[EventA], at), 9);
    }
}

public sealed class JerusalemDayTests
{
    [Theory]
    [InlineData(2026, 9, 25, 21)]
    [InlineData(2026, 12, 25, 22)]
    public void Day_starts_at_local_midnight(int year, int month, int day, int previousDayUtcHour)
    {
        var start = JerusalemDays.StartUtc(new DateOnly(year, month, day));
        Assert.Equal(previousDayUtcHour, start.UtcDateTime.Hour);
        Assert.Equal(new DateOnly(year, month, day), JerusalemDays.Of(start));
        Assert.Equal(new DateOnly(year, month, day).AddDays(-1), JerusalemDays.Of(start.AddTicks(-1)));
    }

    [Fact]
    public void Hourly_series_is_zero_filled()
    {
        var first = new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);
        var id = Guid.NewGuid();
        var series = OwnerAnalyticsService.Series(
            [new EventStatsHourly { EventId = id, HourUtc = first.AddHours(2), DetailViews = 4 }],
            first,
            first.AddHours(3),
            stats => stats.DetailViews);
        Assert.Equal([0, 0, 4, 0], series);
    }
}

public sealed class SyntheticTrafficTests
{
    private static readonly DateTimeOffset Until = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private static List<SimulatedEvent> Catalog()
    {
        var random = new Random(8);
        var categories = Enum.GetValues<EventCategory>();
        return Enumerable.Range(0, 40).Select(index => new SimulatedEvent(
            new Guid(index + 1, 0, 0, new byte[8]),
            categories[index % categories.Length],
            32.7 + random.NextDouble() * 0.6,
            35.1 + random.NextDouble() * 0.6,
            index % 3 == 0 ? 0 : 50,
            Until.AddDays(1 + index % 14),
            index % 10 == 0)).ToList();
    }

    private static readonly (double, double)[] Homes = [(33.2, 35.57), (32.96, 35.5), (32.79, 35.53)];

    private static SyntheticTrafficResult Generate(int seed = 2026) =>
        SyntheticTraffic.Generate(Catalog(), Homes, Until, new SyntheticTrafficOptions { Visitors = 300, Days = 20, Seed = seed, IncludeBursts = false });

    [Fact]
    public void The_same_seed_reproduces_the_same_traffic()
    {
        var first = Generate();
        var second = Generate();
        Assert.Equal(first.Interactions, second.Interactions);
        Assert.NotEqual(first.Interactions.Count, Generate(seed: 7).Interactions.Count);
    }

    [Fact]
    public void Follows_the_deduplication_rule_and_stays_before_the_cut_off()
    {
        var traffic = Generate();
        Assert.All(traffic.Interactions, row => Assert.True(row.OccurredAtUtc < Until));
        // Impressions are deduplicated per surface (feed, picks); everything else per type.
        foreach (var group in traffic.Interactions.GroupBy(row => (row.VisitorId, row.EventId, row.Type, row.Type == InteractionType.Impression ? row.Source : default)))
        {
            var times = group.Select(row => row.OccurredAtUtc).Order().ToList();
            for (var index = 1; index < times.Count; index++) Assert.True(times[index] - times[index - 1] >= TimeSpan.FromMinutes(30));
        }
    }

    [Fact]
    public void Feed_click_through_falls_with_position()
    {
        var feed = Generate().Interactions.Where(row => row.Source == InteractionSource.Feed).ToList();
        double Rate(int position)
        {
            var shown = feed.Count(row => row.Type == InteractionType.Impression && row.Position == position);
            var opened = feed.Count(row => row.Type == InteractionType.DetailView && row.Position == position);
            return (double)opened / shown;
        }

        Assert.True(Rate(1) > Rate(6));
        Assert.True(Rate(2) > Rate(10));
    }

    [Fact]
    public void Personas_have_normalised_sparse_tastes()
    {
        var personas = Generate().Personas;
        Assert.All(personas, persona => Assert.Equal(1, persona.CategoryAffinity.Sum(), 9));
        // Dirichlet(0.4) is sparse: the favourite category usually carries a large share.
        Assert.True(personas.Average(persona => persona.CategoryAffinity.Max()) > 0.4);
    }
}

public sealed class MetricsAccessTests
{
    private static Microsoft.AspNetCore.Http.DefaultHttpContext Context(string address, string? authorization = null)
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(address);
        if (authorization is not null) context.Request.Headers.Authorization = authorization;
        return context;
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.18.0.5", true)]
    [InlineData("192.168.1.9", true)]
    [InlineData("::ffff:172.17.0.1", true)]
    [InlineData("fd00::1", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("172.32.0.1", false)]
    public void Recognises_private_addresses(string address, bool expected)
    {
        Assert.Equal(expected, NorthLife.Api.Health.MetricsAccess.IsPrivate(System.Net.IPAddress.Parse(address)));
    }

    [Fact]
    public void Private_networks_are_refused_unless_allowed()
    {
        var options = new NorthLife.Api.Health.MetricsAccessOptions();
        Assert.False(NorthLife.Api.Health.MetricsAccess.IsAllowed(Context("10.0.0.2"), options));
        options.AllowPrivateNetwork = true;
        Assert.True(NorthLife.Api.Health.MetricsAccess.IsAllowed(Context("10.0.0.2"), options));
        Assert.False(NorthLife.Api.Health.MetricsAccess.IsAllowed(Context("8.8.8.8"), options));
    }

    [Fact]
    public void A_configured_token_is_required_from_everyone()
    {
        var options = new NorthLife.Api.Health.MetricsAccessOptions { Token = "scrape-secret", AllowPrivateNetwork = true };
        Assert.False(NorthLife.Api.Health.MetricsAccess.IsAllowed(Context("127.0.0.1"), options));
        Assert.False(NorthLife.Api.Health.MetricsAccess.IsAllowed(Context("127.0.0.1", "Bearer wrong"), options));
        Assert.True(NorthLife.Api.Health.MetricsAccess.IsAllowed(Context("8.8.8.8", "Bearer scrape-secret"), options));
    }
}
