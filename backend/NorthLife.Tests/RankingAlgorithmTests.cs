using NorthLife.Api.Analytics;
using NorthLife.Api.Models;
using NorthLife.Api.Ranking;
using Xunit.Abstractions;

namespace NorthLife.Tests;

public sealed class GeohashTests
{
    [Fact]
    public void Encodes_the_reference_example()
    {
        // The worked example from Niemeyer's geohash.org and Wikipedia.
        Assert.Equal("u4pruydqqvj", Geohash.Encode(57.64911, 10.40744, 11));
    }

    [Theory]
    [InlineData(33.2073, 35.5700)]
    [InlineData(32.7922, 35.5312)]
    [InlineData(-33.8688, 151.2093)]
    [InlineData(0, 0)]
    [InlineData(89.9, -179.9)]
    public void Decoded_cell_contains_the_point_and_shrinks_with_precision(double latitude, double longitude)
    {
        var previousArea = double.MaxValue;
        for (var precision = 1; precision <= 9; precision++)
        {
            var bounds = Geohash.Decode(Geohash.Encode(latitude, longitude, precision));
            Assert.InRange(latitude, bounds.MinLatitude, bounds.MaxLatitude);
            Assert.InRange(longitude, bounds.MinLongitude, bounds.MaxLongitude);
            var area = (bounds.MaxLatitude - bounds.MinLatitude) * (bounds.MaxLongitude - bounds.MinLongitude);
            Assert.True(area < previousArea);
            previousArea = area;
        }
    }

    [Fact]
    public void Nearby_points_can_straddle_a_cell_edge_but_stay_in_the_neighbour_block()
    {
        // Kiryat Shmona and Tel Hai are 3 km apart yet fall in different precision-4 cells: the
        // edge case that makes searching the 3x3 block necessary.
        var kiryatShmona = Geohash.Encode(33.2073, 35.5700, 4);
        var telHai = Geohash.Encode(33.2340, 35.5790, 4);
        var eilat = Geohash.Encode(29.5577, 34.9519, 4);
        Assert.NotEqual(kiryatShmona, telHai);
        Assert.Contains(telHai, Geohash.CellAndNeighbours(kiryatShmona));
        Assert.DoesNotContain(eilat, Geohash.CellAndNeighbours(kiryatShmona));
    }

    [Fact]
    public void Neighbours_are_the_eight_touching_cells()
    {
        var cell = Geohash.Encode(33.2073, 35.5700, 6);
        var block = Geohash.CellAndNeighbours(cell);
        Assert.Equal(9, block.Count);
        Assert.Equal(9, block.Distinct().Count());
        var center = Geohash.Decode(cell);
        const double epsilon = 1e-9;
        foreach (var neighbour in block.Where(candidate => candidate != cell))
        {
            var bounds = Geohash.Decode(neighbour);
            var touchesLatitude = bounds.MaxLatitude >= center.MinLatitude - epsilon && bounds.MinLatitude <= center.MaxLatitude + epsilon;
            var touchesLongitude = bounds.MaxLongitude >= center.MinLongitude - epsilon && bounds.MinLongitude <= center.MaxLongitude + epsilon;
            Assert.True(touchesLatitude && touchesLongitude, neighbour);
        }
    }

    [Fact]
    public void Prefix_upper_bound_follows_byte_order()
    {
        Assert.Equal("sv9{", Geohash.PrefixUpperBound("sv9z"));
        Assert.Equal("sv9c", Geohash.PrefixUpperBound("sv9b"));
        Assert.True(string.CompareOrdinal("sv9zzzzzz", Geohash.PrefixUpperBound("sv9z")) < 0);
    }

    [Fact]
    public void Rejects_invalid_characters()
    {
        Assert.Throws<FormatException>(() => Geohash.Decode("sva")); // 'a' is not in the alphabet
    }
}

public sealed class NearestEventsTests
{
    private static List<(int Id, double Lat, double Lon)> Points(int seed, int count)
    {
        var random = new Random(seed);
        return Enumerable.Range(0, count)
            .Select(id => (id, 32.5 + random.NextDouble() * 0.9, 34.9 + random.NextDouble() * 0.9))
            .ToList();
    }

    private static Func<IReadOnlyList<string>, Task<IReadOnlyList<GeoCandidate<int>>>> Fetch(List<(int Id, double Lat, double Lon)> points)
    {
        var hashed = points.Select(point => (point, Hash: Geohash.Encode(point.Lat, point.Lon))).ToList();
        return prefixes => Task.FromResult<IReadOnlyList<GeoCandidate<int>>>(hashed
            .Where(entry => prefixes.Any(prefix => entry.Hash.StartsWith(prefix, StringComparison.Ordinal)))
            .Select(entry => new GeoCandidate<int>(entry.point.Id, entry.point.Lat, entry.point.Lon))
            .ToList());
    }

    [Fact]
    public async Task Matches_brute_force_on_random_data()
    {
        var points = Points(3, 800);
        var fetch = Fetch(points);
        var random = new Random(11);
        for (var trial = 0; trial < 60; trial++)
        {
            var (lat, lon) = (32.6 + random.NextDouble() * 0.7, 35.0 + random.NextDouble() * 0.7);
            var k = 1 + random.Next(20);
            var expected = points
                .OrderBy(point => Haversine.DistanceKm(lat, lon, point.Lat, point.Lon))
                .Take(k)
                .Select(point => point.Id)
                .ToList();

            var result = await NearestEvents.FindAsync(lat, lon, k, 200, fetch);

            Assert.Equal(expected, result.Items.Select(item => item.Item).ToList());
            Assert.True(result.Items.Zip(result.Items.Skip(1)).All(pair => pair.First.DistanceKm <= pair.Second.DistanceKm));
        }
    }

    [Fact]
    public async Task Stops_at_the_maximum_radius()
    {
        var points = new List<(int, double, double)> { (1, 33.2073, 35.5700), (2, 33.2340, 35.5790), (3, 32.7922, 35.5312) };
        var result = await NearestEvents.FindAsync(33.21, 35.57, 10, 10, Fetch(points));
        // Tiberias is about 46 km away, outside the 10 km radius.
        Assert.Equal([1, 2], result.Items.Select(item => item.Item).Order().ToList());
    }

    [Fact]
    public async Task Dense_areas_finish_at_a_fine_precision()
    {
        var random = new Random(5);
        var dense = Enumerable.Range(0, 400).Select(id => (id, 33.2073 + (random.NextDouble() - 0.5) * 0.02, 35.57 + (random.NextDouble() - 0.5) * 0.02)).ToList();
        var result = await NearestEvents.FindAsync(33.2073, 35.57, 5, 50, Fetch(dense));
        Assert.Equal(5, result.Items.Count);
        Assert.Equal(NearestEvents.FinestPrecision, result.Precision);
    }
}

public sealed class PositionBiasTests(ITestOutputHelper output)
{
    [Fact]
    public void Recovers_the_simulated_attention_curve_better_than_raw_click_through()
    {
        var catalogue = Enumerable.Range(0, 60).Select(index => new SimulatedEvent(
            new Guid(index + 1, 0, 0, new byte[8]),
            (EventCategory)(index % 8),
            32.7 + index % 7 * 0.08,
            35.1 + index % 5 * 0.1,
            index % 3 == 0 ? 0 : 40,
            new DateTimeOffset(2026, 9, 26, 18, 0, 0, TimeSpan.Zero).AddHours(index * 5),
            index % 9 == 0)).ToList();
        var traffic = SyntheticTraffic.Generate(
            catalogue,
            [(33.2, 35.57), (32.96, 35.5), (32.79, 35.53), (32.92, 35.08)],
            new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero),
            new SyntheticTrafficOptions { Visitors = 2500, Days = 30, Seed = 99, IncludeBursts = false });

        var feed = traffic.Interactions.Where(row => row.Source == InteractionSource.Feed && row.Position is not null).ToList();
        var cells = feed
            .GroupBy(row => (row.EventId, Position: (int)row.Position!.Value, Context: row.ContextKey ?? 0))
            .Select(group => new PositionCell(
                group.Key.EventId,
                group.Key.Position,
                group.Count(row => row.Type == InteractionType.Impression),
                group.Count(row => row.Type == InteractionType.DetailView),
                group.Key.Context))
            .ToList();

        var estimate = PositionBiasEstimator.Estimate(cells, maxPosition: 10);
        double emError = 0, naiveError = 0;
        output.WriteLine("position  truth   EM      naive   impressions");
        foreach (var row in estimate.Where(row => row.Position <= 8))
        {
            var truth = Math.Pow(row.Position, -0.6);
            emError += Math.Abs(row.Propensity - truth);
            naiveError += Math.Abs(row.NaiveRatio - truth);
            output.WriteLine($"{row.Position,8}  {truth:0.000}   {row.Propensity:0.000}   {row.NaiveRatio:0.000}   {row.Impressions}");
        }

        output.WriteLine($"mean absolute error: EM {emError / 8:0.000}, naive {naiveError / 8:0.000}");
        Assert.True(emError / 8 < 0.1, $"EM error {emError / 8:0.000}");
        Assert.True(emError < naiveError);
    }

    [Fact]
    public void Separates_position_from_appeal_on_a_textbook_case()
    {
        // Two events, each shown equally often at positions 1 and 2. True θ = (1, 0.5), α = (0.4, 0.2).
        var hit = Guid.NewGuid();
        var dud = Guid.NewGuid();
        PositionCell Cell(Guid id, int position, double theta, double alpha) => new(id, position, 10_000, (int)(10_000 * theta * alpha));
        var estimate = PositionBiasEstimator.Estimate([Cell(hit, 1, 1, 0.4), Cell(hit, 2, 0.5, 0.4), Cell(dud, 1, 1, 0.2), Cell(dud, 2, 0.5, 0.2)]);
        Assert.Equal(0.5, estimate.Single(row => row.Position == 2).Propensity, 2);
    }

    [Fact]
    public void Isotonic_regression_pools_violations_by_weight()
    {
        // 0.6 then 0.8 violates "never increasing"; pooled with weights 3 and 1 they become 0.65.
        var smoothed = IsotonicRegression.NonIncreasing([1.0, 0.6, 0.8, 0.4], [10, 3, 1, 5]);
        Assert.Equal([1.0, 0.65, 0.65, 0.4], smoothed.Select(value => Math.Round(value, 6)).ToArray());
        Assert.Equal([3.0, 2.0, 1.0], IsotonicRegression.NonIncreasing([3.0, 2.0, 1.0], [1, 1, 1]));
    }

    [Fact]
    public void Smoothed_propensities_never_increase_with_position()
    {
        var events = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToList();
        var random = new Random(4);
        var cells = new List<PositionCell>();
        foreach (var id in events)
        {
            for (var position = 1; position <= 8; position++)
            {
                cells.Add(new PositionCell(id, position, 40, random.Next(0, 8)));
            }
        }

        var estimate = PositionBiasEstimator.Estimate(cells);
        Assert.Equal(1, estimate[0].Propensity, 9);
        Assert.True(estimate.Zip(estimate.Skip(1)).All(pair => pair.Second.Propensity <= pair.First.Propensity + 1e-12));
    }

    [Fact]
    public void Inverse_weights_are_clipped()
    {
        var table = new PropensityTable(new Dictionary<int, double> { [1] = 1, [2] = 0.5, [10] = 0.05 });
        Assert.Equal(1, table.InverseWeight(1));
        Assert.Equal(2, table.InverseWeight(2));
        Assert.Equal(PropensityTable.MaxWeight, table.InverseWeight(10));
        Assert.Equal(1, table.InverseWeight(null));
        Assert.Equal(1, table.InverseWeight(40));
    }
}

public sealed class HotScoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 18, 0, 0, TimeSpan.Zero);
    private static readonly RankingOptions Options = new();

    private static HotCandidate Candidate(int id, double startHours, double? popularityLog = null, bool highlighted = false, double latitude = 33.2, double longitude = 35.57) =>
        new(new Guid(id, 0, 0, new byte[8]), Now.AddHours(startHours), Now.AddHours(startHours + 3), latitude, longitude, highlighted, popularityLog);

    [Fact]
    public void A_running_event_scores_full_proximity()
    {
        var ranked = HotScore.Rank([Candidate(1, -1), Candidate(2, 30)], Now, Options);
        Assert.Equal(1, ranked.Single(pair => pair.Candidate.Id == new Guid(1, 0, 0, new byte[8])).Parts.Proximity);
        Assert.Equal(new Guid(1, 0, 0, new byte[8]), ranked[0].Candidate.Id);
    }

    [Fact]
    public void Recent_engagement_beats_old_engagement()
    {
        // Same raw engagement, two days apart: the older one has decayed to 1/256.
        var recent = DecayedPopularity.LogContribution(100, Now.AddHours(-1));
        var old = DecayedPopularity.LogContribution(100, Now.AddHours(-49));
        var ranked = HotScore.Rank([Candidate(1, 10, old), Candidate(2, 10, recent)], Now, Options);
        Assert.Equal(new Guid(2, 0, 0, new byte[8]), ranked[0].Candidate.Id);
    }

    [Fact]
    public void Distance_counts_only_with_a_location_and_scores_stay_within_one()
    {
        var near = Candidate(1, 10, latitude: 33.2, longitude: 35.57);
        var far = Candidate(2, 10, latitude: 32.5, longitude: 35.0);
        var withoutLocation = HotScore.Rank([near, far], Now, Options);
        Assert.Equal(withoutLocation[0].Parts.Score, withoutLocation[1].Parts.Score, 9);

        var withLocation = HotScore.Rank([far, near], Now, Options, (33.21, 35.57));
        Assert.Equal(near.Id, withLocation[0].Candidate.Id);
        Assert.All(withLocation, pair => Assert.InRange(pair.Parts.Score, 0, 1));
    }

    [Fact]
    public void Editors_picks_get_a_small_boost()
    {
        var ranked = HotScore.Rank([Candidate(1, 10), Candidate(2, 10, highlighted: true)], Now, Options);
        Assert.Equal(new Guid(2, 0, 0, new byte[8]), ranked[0].Candidate.Id);
    }
}
