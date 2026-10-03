namespace NorthLife.Api.Recommendations;

public readonly record struct CoEngagement(double Cosine, int CoVisitors);

/// <summary>
/// Item-item collaborative filtering (Sarwar et al., 2001; Linden, Smith and York, 2003): two events
/// are similar when the same visitors engage with both. Each event is a vector over visitors whose
/// entries are the visitor's engagement weight (opening 3, sharing 4, navigating 5); similarity is
/// the cosine of those vectors. It is accumulated visitor by visitor, touching only pairs a visitor
/// actually shares, so the cost is O(Σ_u |I_u|²) rather than O(items² × visitors). The busiest
/// visitors are capped at their 50 strongest events, which also blunts automated traffic.
/// </summary>
public static class Collaborative
{
    public const int MaxItemsPerVisitor = 50;

    public static Dictionary<(Guid, Guid), CoEngagement> ItemSimilarities(IReadOnlyDictionary<Guid, Dictionary<Guid, double>> visitorItems)
    {
        var dots = new Dictionary<(Guid, Guid), (double Dot, int Count)>();
        var norms = new Dictionary<Guid, double>();

        foreach (var items in visitorItems.Values)
        {
            var strongest = items.Count > MaxItemsPerVisitor
                ? items.OrderByDescending(pair => pair.Value).Take(MaxItemsPerVisitor).ToList()
                : items.ToList();
            foreach (var (item, weight) in strongest) norms[item] = norms.GetValueOrDefault(item) + weight * weight;
            for (var a = 0; a < strongest.Count; a++)
            {
                for (var b = a + 1; b < strongest.Count; b++)
                {
                    var key = Order(strongest[a].Key, strongest[b].Key);
                    var current = dots.GetValueOrDefault(key);
                    dots[key] = (current.Dot + strongest[a].Value * strongest[b].Value, current.Count + 1);
                }
            }
        }

        return dots.ToDictionary(
            pair => pair.Key,
            pair => new CoEngagement(pair.Value.Dot / Math.Sqrt(norms[pair.Key.Item1] * norms[pair.Key.Item2]), pair.Value.Count));
    }

    public static CoEngagement Lookup(IReadOnlyDictionary<(Guid, Guid), CoEngagement> similarities, Guid a, Guid b) =>
        similarities.TryGetValue(Order(a, b), out var value) ? value : default;

    private static (Guid, Guid) Order(Guid a, Guid b) => a.CompareTo(b) < 0 ? (a, b) : (b, a);
}
