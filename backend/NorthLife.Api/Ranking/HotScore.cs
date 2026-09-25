using NorthLife.Api.Analytics;

namespace NorthLife.Api.Ranking;

public sealed class RankingOptions
{
    public const string SectionName = "Ranking";

    /// <summary>Weight of decayed, position-corrected popularity.</summary>
    public double PopularityWeight { get; set; } = 0.5;

    /// <summary>Weight of how soon the event starts (1 while it is running).</summary>
    public double ProximityWeight { get; set; } = 0.3;

    /// <summary>Weight of distance from the visitor; used only when the visitor shares a location.</summary>
    public double DistanceWeight { get; set; } = 0.15;

    /// <summary>Weight of the editors' pick flag.</summary>
    public double EditorWeight { get; set; } = 0.05;

    /// <summary>Start-time scale: an event 24 hours away scores e^−1 ≈ 0.37 on proximity.</summary>
    public double ProximityHours { get; set; } = 24;

    /// <summary>Distance scale: an event 20 km away scores e^−1 on distance.</summary>
    public double DistanceKm { get; set; } = 20;

    /// <summary>Share of first "hot" pages whose top <see cref="ExplorationDepth"/> is shuffled.</summary>
    public double ExplorationRate { get; set; } = 0.1;

    public int ExplorationDepth { get; set; } = 8;

    /// <summary>"Near me" looks no further than this.</summary>
    public double NearMaxRadiusKm { get; set; } = 150;
}

public sealed record HotCandidate(
    Guid Id,
    DateTimeOffset StartAtUtc,
    DateTimeOffset EndAtUtc,
    double Latitude,
    double Longitude,
    bool IsHighlighted,
    double? PopularityLog);

public sealed record HotScoreParts(double Score, double Popularity, double Proximity, double Distance, double Editor);

/// <summary>
/// "Hot now": hot = w_p · popularity + w_t · proximity + w_d · distance + w_e · editor, each term in
/// [0, 1].
/// <list type="bullet">
/// <item>popularity = ln(1 + pop) / ln(1 + pop_max), where pop is the decayed popularity at this moment
/// (so a long tail of old interactions fades) and pop_max is the largest among the candidates.</item>
/// <item>proximity = 1 while running, otherwise e^(−hours to start / 24).</item>
/// <item>distance = e^(−km / 20), only with the visitor's location.</item>
/// <item>editor = 1 for editors' picks.</item>
/// </list>
/// Without a location the distance weight is dropped and the others are rescaled to sum to 1, so
/// scores stay comparable. Scoring n candidates is O(n), sorting O(n log n).
/// </summary>
public static class HotScore
{
    public static IReadOnlyList<(HotCandidate Candidate, HotScoreParts Parts)> Rank(
        IReadOnlyList<HotCandidate> candidates,
        DateTimeOffset now,
        RankingOptions options,
        (double Latitude, double Longitude)? visitor = null)
    {
        var popularity = candidates.ToDictionary(
            candidate => candidate.Id,
            candidate => candidate.PopularityLog is { } log ? DecayedPopularity.ScoreAt(log, now) : 0);
        var maxLog = Math.Log(1 + popularity.Values.DefaultIfEmpty(0).Max());

        var distanceWeight = visitor is null ? 0 : options.DistanceWeight;
        var total = options.PopularityWeight + options.ProximityWeight + options.EditorWeight + distanceWeight;
        if (total <= 0) total = 1;

        return candidates
            .Select(candidate =>
            {
                var pop = maxLog > 0 ? Math.Log(1 + popularity[candidate.Id]) / maxLog : 0;
                var hours = (candidate.StartAtUtc - now).TotalHours;
                var proximity = candidate.StartAtUtc <= now && candidate.EndAtUtc > now ? 1 : Math.Exp(-Math.Max(0, hours) / options.ProximityHours);
                var distance = visitor is { } location
                    ? Math.Exp(-Haversine.DistanceKm(location.Latitude, location.Longitude, candidate.Latitude, candidate.Longitude) / options.DistanceKm)
                    : 0;
                var editor = candidate.IsHighlighted ? 1 : 0;
                var score = (options.PopularityWeight * pop + options.ProximityWeight * proximity + distanceWeight * distance + options.EditorWeight * editor) / total;
                return (candidate, new HotScoreParts(score, pop, proximity, distance, editor));
            })
            .OrderByDescending(pair => pair.Item2.Score)
            .ThenBy(pair => pair.candidate.StartAtUtc)
            .ThenBy(pair => pair.candidate.Id)
            .ToList();
    }
}
