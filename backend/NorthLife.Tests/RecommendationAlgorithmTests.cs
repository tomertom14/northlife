using NorthLife.Api.Analytics;
using NorthLife.Api.Models;
using NorthLife.Api.Recommendations;

namespace NorthLife.Tests;

public sealed class HebrewTextTests
{
    [Theory]
    [InlineData("שָׁלוֹם", "שלומ")]
    [InlineData("ערב ג׳אז!", "ערב גאז ")]
    [InlineData("ים־תיכוני", "ימ תיכוני")]
    [InlineData("קפה, מאפה ומיץ", "קפה  מאפה ומיצ")]
    [InlineData("Jazz Night", "jazz night")]
    public void Normalises_marks_final_letters_and_punctuation(string input, string expected)
    {
        Assert.Equal(expected, HebrewText.Normalize(input));
    }

    [Fact]
    public void Tokens_drop_stop_words_and_single_letters()
    {
        Assert.Equal(["ערב", "גאז", "גג"], HebrewText.Tokens("ערב ג׳אז על הגג של ו").Select(token => token == "הגג" ? "גג" : token).ToList());
    }

    [Fact]
    public void Strips_prefixes_only_when_the_rest_is_a_corpus_word()
    {
        var vocabulary = new HashSet<string> { "הופעה", "גליל", "מוזיקה", "ערב" };
        Assert.Equal("הופעה", HebrewText.Stem("והופעה", vocabulary));
        Assert.Equal("גליל", HebrewText.Stem("בגליל", vocabulary));
        Assert.Equal("גליל", HebrewText.Stem("והגליל", vocabulary));
        // "וזיקה" never occurs alone, so "מוזיקה" is not split.
        Assert.Equal("מוזיקה", HebrewText.Stem("מוזיקה", vocabulary));
        // Too short to strip.
        Assert.Equal("בו", HebrewText.Stem("בו", vocabulary));
        // Not a prefix letter.
        Assert.Equal("גליל", HebrewText.Stem("גליל", vocabulary));
    }
}

public sealed class TfIdfTests
{
    private static ItemText Item(int id, string title, string description, EventCategory category = EventCategory.Music, string locality = "צפת", params string[] tags) =>
        new(new Guid(id, 0, 0, new byte[8]), title, description, tags, category, locality);

    [Fact]
    public void Identical_texts_are_fully_similar_and_vectors_are_unit_length()
    {
        var model = TfIdfModel.Build([Item(1, "ערב ג׳אז על הגג", "טריו ג׳אז"), Item(2, "ערב ג׳אז על הגג", "טריו ג׳אז"), Item(3, "שייט קיאקים", "נהר הירדן", EventCategory.Outdoors, "קצרין")]);
        var a = new Guid(1, 0, 0, new byte[8]);
        var b = new Guid(2, 0, 0, new byte[8]);
        Assert.Equal(1, model.Cosine(a, b), 9);
        Assert.Equal(1, model.Vectors[a].Values.Sum(value => value * value), 9);
    }

    [Fact]
    public void Shared_specific_words_matter_more_than_shared_common_ones()
    {
        var model = TfIdfModel.Build([
            Item(1, "ערב ג׳אז", "הופעה בעיר"),
            Item(2, "ג׳אם ג׳אז", "הופעה בעיר"),
            Item(3, "ערב שירה", "הופעה בעיר"),
            Item(4, "ערב מחול", "הופעה בעיר"),
        ]);
        var jazz = new Guid(1, 0, 0, new byte[8]);
        // "ג׳אז" is shared by two events only; "ערב" by three.
        Assert.True(model.Cosine(jazz, new Guid(2, 0, 0, new byte[8])) > model.Cosine(jazz, new Guid(3, 0, 0, new byte[8])));
    }

    [Fact]
    public void Category_and_town_act_as_features_and_unrelated_events_score_zero()
    {
        var model = TfIdfModel.Build([
            Item(1, "אאא", "בבב", EventCategory.Food, "עכו"),
            Item(2, "גגג", "דדד", EventCategory.Food, "עכו"),
            Item(3, "ההה", "ווו", EventCategory.Sports, "טבריה"),
        ]);
        Assert.True(model.Cosine(new Guid(1, 0, 0, new byte[8]), new Guid(2, 0, 0, new byte[8])) > 0);
        Assert.Equal(0, model.Cosine(new Guid(1, 0, 0, new byte[8]), new Guid(3, 0, 0, new byte[8])));
    }
}

public sealed class CollaborativeTests
{
    private static readonly Guid A = new(1, 0, 0, new byte[8]);
    private static readonly Guid B = new(2, 0, 0, new byte[8]);
    private static readonly Guid C = new(3, 0, 0, new byte[8]);

    [Fact]
    public void Events_engaged_by_the_same_visitors_are_similar()
    {
        var visitors = new Dictionary<Guid, Dictionary<Guid, double>>
        {
            [Guid.NewGuid()] = new() { [A] = 3, [B] = 3 },
            [Guid.NewGuid()] = new() { [A] = 5, [B] = 5 },
            [Guid.NewGuid()] = new() { [C] = 3 },
        };
        var similarities = Collaborative.ItemSimilarities(visitors);
        var ab = Collaborative.Lookup(similarities, B, A);
        Assert.Equal(1, ab.Cosine, 9);
        Assert.Equal(2, ab.CoVisitors);
        Assert.Equal(0, Collaborative.Lookup(similarities, A, C).CoVisitors);
    }

    [Fact]
    public void Blend_trusts_text_without_shared_visitors_and_behaviour_with_many()
    {
        var options = new RecommendationOptions { BlendGamma = 10 };
        Assert.Equal(0.8, SimilarityBlender.Mix(0.8, 0, 0, options), 9);
        var busy = SimilarityBlender.Mix(0.1, 0.9, 90, options);
        Assert.True(busy > 0.8, $"{busy}");
        Assert.Equal(0.9, SimilarityBlender.Mix(0.1, 0.9, 0, new RecommendationOptions { BlendGamma = 0 }), 9);
    }
}

public sealed class RecommenderTests
{
    private static Guid Id(int value) => new(value, 0, 0, new byte[8]);

    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Profile_weights_engagement_and_decays_with_its_half_life()
    {
        var options = new RecommendationOptions { ProfileHalfLifeDays = 3 };
        var visitor = Guid.NewGuid();
        var profile = Recommender.Profile(
        [
            new RawInteraction(Id(1), visitor, InteractionType.Navigate, InteractionSource.Details, null, Now),
            new RawInteraction(Id(2), visitor, InteractionType.DetailView, InteractionSource.Feed, 1, Now.AddDays(-3)),
            new RawInteraction(Id(3), visitor, InteractionType.Impression, InteractionSource.Feed, 1, Now),
        ], Now, options);
        Assert.Equal(5, profile[Id(1)], 9);
        Assert.Equal(1.5, profile[Id(2)], 9);
        Assert.False(profile.ContainsKey(Id(3)));
    }

    [Fact]
    public void Recommends_neighbours_of_the_profile_with_a_reason_and_skips_what_was_seen()
    {
        var neighbours = new Dictionary<Guid, IReadOnlyList<Neighbour>>
        {
            [Id(1)] = [new(Id(2), 0.9, 0, 0.9, 0), new(Id(3), 0.5, 0, 0.5, 0), new(Id(9), 0.95, 0, 0.95, 0)],
        };
        var picks = Recommender.Recommend(
            new Dictionary<Guid, double> { [Id(1)] = 3 },
            id => neighbours.GetValueOrDefault(id) ?? [],
            new HashSet<Guid> { Id(2), Id(3), Id(4), Id(9) },
            new HashSet<Guid> { Id(9) },
            new Dictionary<Guid, double> { [Id(4)] = 1 },
            (_, _) => 0,
            3,
            new RecommendationOptions { PopularityWeight = 0.2, MmrLambda = 1 });
        Assert.Equal(Id(2), picks[0].EventId);
        Assert.Equal(Id(1), picks[0].BecauseOf);
        Assert.Equal("similar", picks[0].Kind);
        Assert.DoesNotContain(picks, pick => pick.EventId == Id(9));
        Assert.Contains(picks, pick => pick.EventId == Id(4) && pick.Kind == "popular");
    }

    [Fact]
    public void A_visitor_without_history_gets_the_most_popular_events()
    {
        var picks = Recommender.Recommend(
            new Dictionary<Guid, double>(),
            _ => [],
            new HashSet<Guid> { Id(1), Id(2), Id(3) },
            new HashSet<Guid>(),
            new Dictionary<Guid, double> { [Id(1)] = 0.2, [Id(2)] = 0.9, [Id(3)] = 0.5 },
            (_, _) => 0,
            2,
            new RecommendationOptions { MmrLambda = 1 });
        Assert.Equal([Id(2), Id(3)], picks.Select(pick => pick.EventId).ToList());
        Assert.All(picks, pick => Assert.Equal("popular", pick.Kind));
    }

    [Fact]
    public void Mmr_trades_a_little_relevance_for_variety()
    {
        var candidates = new List<(Guid, double)> { (Id(1), 1.0), (Id(2), 0.98), (Id(3), 0.7) };
        // 1 and 2 are near duplicates.
        double Similarity(Guid a, Guid b) => (a == Id(1) && b == Id(2)) || (a == Id(2) && b == Id(1)) ? 0.95 : 0;
        Assert.Equal([Id(1), Id(2)], Recommender.Mmr(candidates, Similarity, 2, 1).Select(pick => pick.Id).ToList());
        Assert.Equal([Id(1), Id(3)], Recommender.Mmr(candidates, Similarity, 2, 0.7).Select(pick => pick.Id).ToList());
    }

    [Fact]
    public void A_brand_new_event_competes_through_the_optimistic_prior()
    {
        var neighbours = new Dictionary<Guid, IReadOnlyList<Neighbour>> { [Id(1)] = [new(Id(5), 0.9, 0, 0.9, 0), new(Id(6), 0.9, 0, 0.9, 0)] };
        RecommendationOptions Options(double prior) => new() { PopularityWeight = 0.6, NewEventPrior = prior, MmrLambda = 1 };
        IReadOnlyList<Recommendation> Pick(double prior) => Recommender.Recommend(
            new Dictionary<Guid, double> { [Id(1)] = 1 },
            id => neighbours.GetValueOrDefault(id) ?? [],
            new HashSet<Guid> { Id(5), Id(6) },
            new HashSet<Guid>(),
            // Event 6 is known and ranked in the middle; event 5 is new and has no popularity yet.
            new Dictionary<Guid, double> { [Id(6)] = 0.5 },
            (_, _) => 0,
            1,
            Options(prior));
        Assert.Equal(Id(5), Pick(0.9)[0].EventId);
        Assert.Equal(Id(6), Pick(0.1)[0].EventId);
    }

    [Fact]
    public void Popularity_becomes_percentile_ranks_with_shared_ties()
    {
        var scaled = Recommender.ScalePopularity(new Dictionary<Guid, double> { [Id(1)] = 0, [Id(2)] = 0, [Id(3)] = 5, [Id(4)] = 100 });
        Assert.Equal(1.0 / 6, scaled[Id(1)], 9);
        Assert.Equal(scaled[Id(1)], scaled[Id(2)]);
        Assert.Equal(2.0 / 3, scaled[Id(3)], 9);
        Assert.Equal(1, scaled[Id(4)], 9);
    }
}

public sealed class EvaluationMetricTests
{
    private static Guid Id(int value) => new(value, 0, 0, new byte[8]);

    [Fact]
    public void Precision_recall_and_ndcg_match_hand_computed_values()
    {
        var list = new List<Guid> { Id(1), Id(2), Id(3), Id(4) };
        var relevant = new HashSet<Guid> { Id(2), Id(7) };
        Assert.Equal(0.25, OfflineEvaluation.Precision(list, relevant, 4));
        Assert.Equal(0.5, OfflineEvaluation.Recall(list, relevant, 4));
        // DCG = 1 / log2(3); ideal = 1 + 1 / log2(3).
        Assert.Equal((1 / Math.Log2(3)) / (1 + 1 / Math.Log2(3)), OfflineEvaluation.Ndcg(list, relevant, 4), 9);
        Assert.Equal(1, OfflineEvaluation.Ndcg([Id(2), Id(7)], relevant, 2), 9);
    }

    [Fact]
    public void Offline_evaluation_ranks_the_models_sensibly()
    {
        var report = OfflineEvaluation.Run(testSeed: 11, validationSeed: 12, personas: 400);
        double Ndcg(string prefix) => report.Rows.First(row => row.Method.StartsWith(prefix, StringComparison.Ordinal)).NdcgAt10;
        Assert.True(report.TestUsers > 20);
        Assert.True(Ndcg("Blend (") > Ndcg("Random"));
        Assert.True(Ndcg("Oracle") >= Ndcg("Blend ("));
        Assert.Equal(0, report.ColdStartRows.First(row => row.Method.StartsWith("Collaborative", StringComparison.Ordinal)).RecallAt10);
        Assert.Contains("| Method |", report.ToMarkdown());
    }
}
