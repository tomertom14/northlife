using NorthLife.Api.Analytics;

namespace NorthLife.Api.Recommendations;

public sealed class RecommendationOptions
{
    public const string SectionName = "Recommendations";

    /// <summary>Neighbours kept per event.</summary>
    public int Neighbours { get; set; } = 20;

    /// <summary>Significance shrinkage: a collaborative similarity from n shared visitors counts n / (n + β).</summary>
    public double ShrinkageBeta { get; set; } = 5;

    /// <summary>
    /// Blend: α = γ / (γ + n); with few shared visitors content dominates, with many, behaviour does.
    /// Defaults for γ, μ and the new-event prior come from the offline evaluation (tuned on a
    /// validation seed; see docs/evaluation/recommendations.md).
    /// </summary>
    public double BlendGamma { get; set; } = 25;

    /// <summary>MMR trade-off between relevance (1) and diversity (0).</summary>
    public double MmrLambda { get; set; } = 0.8;

    /// <summary>A visitor's interest in an event halves every this many days.</summary>
    public double ProfileHalfLifeDays { get; set; } = 3;

    /// <summary>
    /// μ: weight of the popularity prior against the personal score. Visitors open what they are
    /// shown and what is good, not only what matches their taste, so a little popularity helps.
    /// </summary>
    public double PopularityWeight { get; set; } = 0.6;

    /// <summary>Most popular events added to the candidates besides the profile's neighbours.</summary>
    public int PopularCandidates { get; set; } = 50;

    /// <summary>
    /// Popularity percentile assumed for an event nobody has interacted with yet. Above the median on
    /// purpose ("optimism in the face of uncertainty", as in UCB bandits): every event starts cold,
    /// and a new one must get some exposure before its real popularity can be learned.
    /// </summary>
    public double NewEventPrior { get; set; } = 0.9;

    /// <summary>How often the worker rebuilds the similarity model.</summary>
    public int RefreshMinutes { get; set; } = 15;
}

public sealed record Neighbour(Guid Target, double Content, double Collaborative, double Blended, int CoVisitors);

public sealed record Recommendation(Guid EventId, double Score, Guid? BecauseOf, string Kind);

/// <summary>Blends content and behaviour into one neighbour list per event.</summary>
public static class SimilarityBlender
{
    /// <summary>
    /// sim(i, j) = α · content(i, j) + (1 − α) · collaborative(i, j), with α = γ / (γ + n_ij), where
    /// collaborative is the cosine shrunk by n_ij / (n_ij + β). New events have no shared visitors, so
    /// they are matched on their text (cold start); as behaviour accumulates it takes over. The two
    /// signals live on different scales (text cosines run higher than behavioural ones), so each is
    /// divided by its largest value for the source before blending; otherwise text alone fills every
    /// list. Top-K per source is kept with a bounded heap, O(|targets| log K) per source.
    /// </summary>
    public static Dictionary<Guid, List<Neighbour>> TopNeighbours(
        IEnumerable<Guid> sources,
        IReadOnlyCollection<Guid> targets,
        TfIdfModel content,
        IReadOnlyDictionary<(Guid, Guid), CoEngagement> collaborative,
        RecommendationOptions options)
    {
        var result = new Dictionary<Guid, List<Neighbour>>();
        var raw = new List<(Guid Target, double Text, double Behaviour, int CoVisitors)>(targets.Count);
        foreach (var source in sources)
        {
            raw.Clear();
            double maxText = 0, maxBehaviour = 0;
            foreach (var target in targets)
            {
                if (target == source) continue;
                var text = content.Cosine(source, target);
                var co = Collaborative.Lookup(collaborative, source, target);
                var behaviour = co.CoVisitors / (co.CoVisitors + options.ShrinkageBeta) * co.Cosine;
                if (text <= 0 && behaviour <= 0) continue;
                raw.Add((target, text, behaviour, co.CoVisitors));
                maxText = Math.Max(maxText, text);
                maxBehaviour = Math.Max(maxBehaviour, behaviour);
            }

            var heap = new PriorityQueue<Neighbour, double>();
            foreach (var (target, text, behaviour, coVisitors) in raw)
            {
                var textScore = maxText > 0 ? text / maxText : 0;
                var behaviourScore = maxBehaviour > 0 ? behaviour / maxBehaviour : 0;
                var neighbour = new Neighbour(target, text, behaviour, Mix(textScore, behaviourScore, coVisitors, options), coVisitors);
                if (neighbour.Blended <= 0) continue;
                heap.Enqueue(neighbour, neighbour.Blended);
                if (heap.Count > options.Neighbours) heap.Dequeue();
            }

            var list = new List<Neighbour>(heap.Count);
            while (heap.TryDequeue(out var neighbour, out _)) list.Add(neighbour);
            list.Reverse();
            result[source] = list;
        }

        return result;
    }

    /// <summary>α · text + (1 − α) · behaviour with α = γ / (γ + n); γ = 0 means behaviour only.</summary>
    public static double Mix(double text, double behaviour, int coVisitors, RecommendationOptions options)
    {
        var alpha = options.BlendGamma + coVisitors == 0 ? 0 : options.BlendGamma / (options.BlendGamma + coVisitors);
        return alpha * text + (1 - alpha) * behaviour;
    }
}

public static class Recommender
{
    /// <summary>
    /// A visitor's profile: every event they engaged with, weighted by engagement and decayed with the
    /// profile half-life, so last night's clicks matter more than last week's.
    /// </summary>
    public static Dictionary<Guid, double> Profile(IEnumerable<RawInteraction> history, DateTimeOffset now, RecommendationOptions options)
    {
        var lambda = Math.Log(2) / TimeSpan.FromDays(options.ProfileHalfLifeDays).TotalHours;
        var profile = new Dictionary<Guid, double>();
        foreach (var row in history)
        {
            var weight = row.Type switch
            {
                InteractionType.DetailView => 3,
                InteractionType.Share => 4,
                InteractionType.Navigate => 5,
                _ => 0.0,
            };
            if (weight == 0) continue;
            profile[row.EventId] = profile.GetValueOrDefault(row.EventId) + weight * Math.Exp(-lambda * Math.Max(0, (now - row.OccurredAtUtc).TotalHours));
        }

        return profile;
    }

    /// <summary>
    /// Candidates are the neighbours of every profile event plus the most popular events, restricted
    /// to eligible events the visitor has not opened yet. Each gets
    /// score(j) = (1 − μ) · personal(j) / max personal + μ · popularity(j),
    /// where personal(j) = Σ_i profile(i) · sim(i, j) and popularity is already scaled to [0, 1]; the
    /// profile event contributing most explains the pick ("because you viewed i"). The list is then
    /// re-ranked with maximal marginal relevance (Carbonell and Goldstein, 1998): each next pick
    /// maximises λ · score − (1 − λ) · (similarity to what is already picked), so five near-identical
    /// jazz nights do not crowd out everything else. With no history, personal is zero everywhere
    /// and the list is simply the most popular events (cold start). O(profile × K + k² · pool).
    /// </summary>
    public static IReadOnlyList<Recommendation> Recommend(
        IReadOnlyDictionary<Guid, double> profile,
        Func<Guid, IReadOnlyList<Neighbour>> neighbours,
        IReadOnlySet<Guid> eligible,
        IReadOnlySet<Guid> exclude,
        IReadOnlyDictionary<Guid, double> popularity,
        Func<Guid, Guid, double> similarity,
        int k,
        RecommendationOptions options)
    {
        var personal = new Dictionary<Guid, (double Score, Guid Because, double Best)>();
        foreach (var (source, weight) in profile)
        {
            foreach (var neighbour in neighbours(source))
            {
                if (!eligible.Contains(neighbour.Target) || exclude.Contains(neighbour.Target)) continue;
                var contribution = weight * neighbour.Blended;
                var current = personal.GetValueOrDefault(neighbour.Target);
                personal[neighbour.Target] = contribution > current.Best
                    ? (current.Score + contribution, source, contribution)
                    : (current.Score + contribution, current.Because, current.Best);
            }
        }

        var candidates = personal.Keys.ToHashSet();
        foreach (var (id, _) in popularity.OrderByDescending(pair => pair.Value).Take(options.PopularCandidates))
        {
            if (eligible.Contains(id) && !exclude.Contains(id)) candidates.Add(id);
        }

        var maxPersonal = personal.Count == 0 ? 0 : personal.Values.Max(value => value.Score);
        var mu = maxPersonal > 0 ? options.PopularityWeight : 1;
        // An event nobody has interacted with yet (just published) gets an optimistic popularity
        // prior instead of zero, so it competes on its content rather than being buried before anyone
        // could see it.
        var scored = candidates
            .Select(id => (Id: id, Score:
                (1 - mu) * (maxPersonal > 0 && personal.TryGetValue(id, out var value) ? value.Score / maxPersonal : 0)
                + mu * (popularity.TryGetValue(id, out var known) ? known : options.NewEventPrior)))
            .ToList();

        return Mmr(scored, similarity, k, options.MmrLambda)
            .Select(pick => personal.TryGetValue(pick.Id, out var value)
                ? new Recommendation(pick.Id, pick.Score, value.Because, "similar")
                : new Recommendation(pick.Id, pick.Score, null, "popular"))
            .ToList();
    }

    /// <summary>
    /// Popularity as a percentile rank in [0, 1] (ties share their average rank). Decayed popularity
    /// is heavy-tailed, so most events sit near zero on any value scale and the median carries no
    /// information; ranks spread them evenly, and the median, used as the prior for unseen events,
    /// is exactly 0.5.
    /// </summary>
    public static Dictionary<Guid, double> ScalePopularity(IReadOnlyDictionary<Guid, double> popularity)
    {
        var ordered = popularity.OrderBy(pair => pair.Value).ToList();
        var result = new Dictionary<Guid, double>(ordered.Count);
        if (ordered.Count == 1) result[ordered[0].Key] = 0.5;
        if (ordered.Count <= 1) return result;
        for (var start = 0; start < ordered.Count;)
        {
            var end = start;
            while (end + 1 < ordered.Count && ordered[end + 1].Value == ordered[start].Value) end++;
            var rank = (start + end) / 2.0 / (ordered.Count - 1);
            for (var index = start; index <= end; index++) result[ordered[index].Key] = rank;
            start = end + 1;
        }

        return result;
    }

    /// <summary>Greedy maximal marginal relevance over candidates scored in [0, max].</summary>
    public static List<(Guid Id, double Score)> Mmr(IReadOnlyList<(Guid Id, double Score)> candidates, Func<Guid, Guid, double> similarity, int k, double lambda)
    {
        var max = candidates.Count == 0 ? 1 : Math.Max(candidates.Max(candidate => candidate.Score), 1e-12);
        var pool = candidates.OrderByDescending(candidate => candidate.Score).Take(Math.Max(k * 5, 50)).ToList();
        var picked = new List<(Guid Id, double Score)>();
        while (picked.Count < k && pool.Count > 0)
        {
            var best = pool
                .Select(candidate => (Candidate: candidate, Value: lambda * candidate.Score / max
                    - (1 - lambda) * (picked.Count == 0 ? 0 : picked.Max(chosen => similarity(candidate.Id, chosen.Id)))))
                .MaxBy(entry => entry.Value);
            picked.Add(best.Candidate);
            pool.Remove(best.Candidate);
        }

        return picked;
    }
}
