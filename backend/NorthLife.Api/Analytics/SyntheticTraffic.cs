using NorthLife.Api.Models;

namespace NorthLife.Api.Analytics;

/// <summary>An event as the simulator sees it.</summary>
public sealed record SimulatedEvent(
    Guid Id,
    EventCategory Category,
    double Latitude,
    double Longitude,
    decimal Price,
    DateTimeOffset StartAtUtc,
    bool IsHighlighted);

/// <summary>A synthetic visitor with known tastes; the recommender evaluation uses them as ground truth.</summary>
public sealed record Persona(
    Guid VisitorId,
    double[] CategoryAffinity,
    double HomeLatitude,
    double HomeLongitude,
    double VisitProbability,
    double PriceSensitivity);

public sealed record SyntheticTrafficOptions
{
    public int Visitors { get; init; } = 600;
    public int Days { get; init; } = 30;
    public int Seed { get; init; } = 2026;
    public int PageSize { get; init; } = 12;

    /// <summary>
    /// Attention paid to feed position k (1-based) among cards on screen: k^−η. This is the
    /// position bias Phase 14 estimates and corrects; the simulator's value is the ground truth.
    /// </summary>
    public double AttentionExponent { get; init; } = 0.6;

    /// <summary>Chance of scrolling on to see the next card.</summary>
    public double ScrollDepth { get; init; } = 0.92;

    /// <summary>Add a sudden surge and a suspicious burst in the last complete hour, for the anomaly views.</summary>
    public bool IncludeBursts { get; init; } = true;

    /// <summary>Share of visits that use the "hot now" sort.</summary>
    public double HotShare { get; init; } = 0.3;

    /// <summary>
    /// Chance that a hot feed page shuffles its top <see cref="ExplorationDepth"/> (randomised top-N,
    /// as the API does), which is what makes position bias identifiable.
    /// </summary>
    public double ExplorationRate { get; init; } = 0.2;

    public int ExplorationDepth { get; init; } = 8;
}

public sealed record SyntheticTrafficResult(IReadOnlyList<RawInteraction> Interactions, IReadOnlyList<Persona> Personas, IReadOnlyDictionary<Guid, double> Appeal);

/// <summary>
/// Simulates visitors browsing the feed, so ranking and recommendations can be developed and
/// evaluated before there is real traffic. Each visitor has category tastes drawn from a sparse
/// Dirichlet distribution, a home town, a visit rate and a price sensitivity; each event has a
/// hidden appeal. On a visit the visitor sees a page of the start-time-ordered feed (sometimes
/// filtered to a favourite category), scrolls with a fixed depth, pays attention to position k in
/// proportion to k^−η (the examination hypothesis), and opens a card with probability
/// attention × relevance. Opening can lead to navigation or sharing. The 30-minute deduplication rule
/// of the real pipeline is applied, so the output looks exactly like recorded traffic.
/// </summary>
public static class SyntheticTraffic
{
    public static SyntheticTrafficResult Generate(
        IReadOnlyList<SimulatedEvent> events,
        IReadOnlyList<(double Latitude, double Longitude)> homes,
        DateTimeOffset until,
        SyntheticTrafficOptions? options = null)
    {
        options ??= new SyntheticTrafficOptions();
        var random = new Random(options.Seed);
        var categories = Enum.GetValues<EventCategory>();
        var personas = Enumerable.Range(0, options.Visitors).Select(_ => NewPersona(random, homes, categories.Length)).ToList();
        var appeal = events.ToDictionary(item => item.Id, _ => Normal(random) * 0.6);
        var interactions = new List<RawInteraction>();
        var ordered = events.OrderBy(item => item.StartAtUtc).ThenBy(item => item.Id).ToList();
        var lastDay = JerusalemDays.Of(until);

        for (var dayIndex = options.Days - 1; dayIndex >= 0; dayIndex--)
        {
            var day = lastDay.AddDays(-dayIndex);
            var dayStart = JerusalemDays.StartUtc(day);
            // Interest builds up as the events get closer.
            var momentum = 0.5 + 0.7 * (options.Days - dayIndex) / options.Days;
            var upcoming = ordered.Where(item => item.StartAtUtc >= dayStart).ToList();
            if (upcoming.Count == 0) continue;

            foreach (var persona in personas)
            {
                if (random.NextDouble() >= persona.VisitProbability * momentum) continue;
                var sessionStart = dayStart.AddMinutes(8 * 60 + random.Next(15 * 60 + 30));
                if (sessionStart >= until) continue;
                Visit(persona, upcoming, sessionStart, until, options, appeal, random, interactions);
            }
        }

        if (options.IncludeBursts && ordered.Count >= 2) AddBursts(ordered, until, random, interactions);
        return new SyntheticTrafficResult(interactions, personas, appeal);
    }

    /// <summary>
    /// Probability that a persona who notices an event opens it. Exposed so the offline evaluation can
    /// score recommendations against the same ground truth the traffic was drawn from.
    /// </summary>
    public static double Relevance(Persona persona, SimulatedEvent item, double appeal)
    {
        var interest = persona.CategoryAffinity[(int)item.Category] * persona.CategoryAffinity.Length;
        var distance = DistanceKm(persona.HomeLatitude, persona.HomeLongitude, item.Latitude, item.Longitude);
        var logit = -2.0 + 1.2 * Math.Log(interest + 0.05) + appeal - 0.6 * distance / 25 - persona.PriceSensitivity * (double)item.Price / 100;
        return Sigmoid(logit);
    }

    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadiusKm = 6371.0088;
        static double Radians(double degrees) => degrees * Math.PI / 180;
        var dLat = Radians(lat2 - lat1);
        var dLon = Radians(lon2 - lon1);
        var a = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(Radians(lat1)) * Math.Cos(Radians(lat2)) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * earthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    private static void Visit(
        Persona persona,
        List<SimulatedEvent> upcoming,
        DateTimeOffset sessionStart,
        DateTimeOffset until,
        SyntheticTrafficOptions options,
        Dictionary<Guid, double> appeal,
        Random random,
        List<RawInteraction> output)
    {
        // Same rule as the ingestion API: once per event and type in 30 minutes, per surface for impressions.
        var recorded = new HashSet<(Guid, InteractionType, InteractionSource?)>();
        var clock = sessionStart;

        void Record(SimulatedEvent item, InteractionType type, InteractionSource source, short? position, int? context = null)
        {
            clock = clock.AddSeconds(5 + random.Next(40));
            var surface = type == InteractionType.Impression ? source : (InteractionSource?)null;
            if (clock >= until || !recorded.Add((item.Id, type, surface))) return;
            output.Add(new RawInteraction(item.Id, persona.VisitorId, type, source, position, clock, context));
        }

        void AfterOpening(SimulatedEvent item, double relevance)
        {
            var interest = persona.CategoryAffinity[(int)item.Category] * persona.CategoryAffinity.Length;
            var distance = DistanceKm(persona.HomeLatitude, persona.HomeLongitude, item.Latitude, item.Longitude);
            var navigate = Sigmoid(-1.5 + 0.8 * Math.Log(interest + 0.05) - 1.2 * distance / 25 + 0.5 * appeal[item.Id]);
            if (random.NextDouble() < navigate) Record(item, InteractionType.Navigate, InteractionSource.Details, null);
            if (random.NextDouble() < 0.03 + 0.05 * relevance) Record(item, InteractionType.Share, InteractionSource.Details, null);
        }

        // Editors' picks rail: highlighted events, all on screen, flat attention.
        var picks = upcoming.Where(item => item.IsHighlighted).Take(6).ToList();
        for (var index = 0; index < picks.Count; index++)
        {
            if (random.NextDouble() > 0.8) continue;
            var item = picks[index];
            Record(item, InteractionType.Impression, InteractionSource.Picks, (short)(index + 1));
            var relevance = Relevance(persona, item, appeal[item.Id]);
            if (random.NextDouble() < 0.35 * relevance)
            {
                Record(item, InteractionType.DetailView, InteractionSource.Picks, (short)(index + 1));
                AfterOpening(item, relevance);
            }
        }

        // The feed in one of several lists: everything by time, one day, a favourite category, or
        // "hot now". Each list is a context: the audience differs (category lists are read by fans),
        // so click models fit appeal per context and event, like per query and document in search.
        var list = upcoming;
        var contextName = "time|all";
        var context = random.NextDouble();
        if (context < options.HotShare)
        {
            // Hot ordering: popularity follows appeal, with some noise.
            list = upcoming.OrderByDescending(item => appeal[item.Id] + 0.3 * Normal(random)).ToList();
            contextName = "hot|all";
            if (random.NextDouble() < options.ExplorationRate) Ranking.Exploration.ShuffleTop(list, options.ExplorationDepth, random);
        }
        else if (context < options.HotShare + 0.25)
        {
            var favourite = WeightedIndex(persona.CategoryAffinity, random);
            var filtered = upcoming.Where(item => (int)item.Category == favourite).ToList();
            if (filtered.Count > 0)
            {
                list = filtered;
                contextName = $"time|cat:{favourite}";
            }
        }
        else if (context < options.HotShare + 0.5)
        {
            var days = upcoming.Select(item => JerusalemDays.Of(item.StartAtUtc)).Distinct().Take(10).ToList();
            var chosen = days[random.Next(days.Count)];
            list = upcoming.Where(item => JerusalemDays.Of(item.StartAtUtc) == chosen).ToList();
            contextName = $"time|day:{chosen:yyyy-MM-dd}";
        }

        var roll = random.NextDouble();
        var page = roll < 0.6 ? 0 : roll < 0.9 ? 1 : 2;
        var offset = Math.Min(page * options.PageSize, Math.Max(0, list.Count - 1) / options.PageSize * options.PageSize);
        var contextKey = FeedContext.Key($"{contextName}|p{offset / options.PageSize + 1}");
        for (var slot = 0; slot < options.PageSize && offset + slot < list.Count; slot++)
        {
            // Scroll on, or stop here.
            if (slot > 0 && random.NextDouble() > options.ScrollDepth) break;
            var item = list[offset + slot];
            var position = (short)(offset + slot + 1);
            Record(item, InteractionType.Impression, InteractionSource.Feed, position, contextKey);

            var attention = Math.Pow(position, -options.AttentionExponent);
            var relevance = Relevance(persona, item, appeal[item.Id]);
            if (random.NextDouble() < attention * relevance)
            {
                Record(item, InteractionType.DetailView, InteractionSource.Feed, position, contextKey);
                AfterOpening(item, relevance);
            }
        }
    }

    /// <summary>
    /// In the last complete hour: one event gets an organic surge (views that lead to navigation) and
    /// another gets a burst of views that lead nowhere, as automated traffic would.
    /// </summary>
    private static void AddBursts(List<SimulatedEvent> ordered, DateTimeOffset until, Random random, List<RawInteraction> output)
    {
        var hour = new DateTimeOffset(until.UtcDateTime.Date.AddHours(until.UtcDateTime.Hour), TimeSpan.Zero).AddHours(-1);
        var surge = ordered[0];
        var burst = ordered[1];
        for (var visitor = 0; visitor < 60; visitor++)
        {
            var id = NewGuid(random);
            var at = hour.AddSeconds(random.Next(3000));
            output.Add(new RawInteraction(surge.Id, id, InteractionType.DetailView, InteractionSource.Direct, null, at));
            if (visitor % 3 == 0) output.Add(new RawInteraction(surge.Id, id, InteractionType.Navigate, InteractionSource.Details, null, at.AddSeconds(90)));
        }

        for (var visitor = 0; visitor < 70; visitor++)
        {
            output.Add(new RawInteraction(burst.Id, NewGuid(random), InteractionType.DetailView, InteractionSource.Direct, null, hour.AddSeconds(random.Next(3000))));
        }
    }

    private static Persona NewPersona(Random random, IReadOnlyList<(double Latitude, double Longitude)> homes, int categoryCount)
    {
        // Dirichlet(0.4): most visitors care about one to three categories.
        var weights = Enumerable.Range(0, categoryCount).Select(_ => Gamma(random, 0.4)).ToArray();
        var sum = weights.Sum();
        var affinity = weights.Select(weight => weight / sum).ToArray();
        var home = homes[random.Next(homes.Count)];
        return new Persona(
            NewGuid(random),
            affinity,
            home.Latitude + (random.NextDouble() - 0.5) * 0.04,
            home.Longitude + (random.NextDouble() - 0.5) * 0.04,
            0.08 + 0.4 * random.NextDouble() * random.NextDouble(),
            random.NextDouble());
    }

    /// <summary>Ids come from the seeded generator, so the same seed reproduces the same data set.</summary>
    private static Guid NewGuid(Random random)
    {
        var bytes = new byte[16];
        random.NextBytes(bytes);
        return new Guid(bytes);
    }

    private static int WeightedIndex(double[] weights, Random random)
    {
        var target = random.NextDouble() * weights.Sum();
        for (var index = 0; index < weights.Length; index++)
        {
            target -= weights[index];
            if (target <= 0) return index;
        }

        return weights.Length - 1;
    }

    private static double Sigmoid(double value) => 1 / (1 + Math.Exp(-value));

    /// <summary>Standard normal variate (Box–Muller).</summary>
    private static double Normal(Random random)
    {
        var u1 = 1 - random.NextDouble();
        var u2 = random.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }

    /// <summary>Gamma(shape, 1) variate (Marsaglia and Tsang, 2000), with the shape &lt; 1 boost.</summary>
    private static double Gamma(Random random, double shape)
    {
        if (shape < 1) return Gamma(random, shape + 1) * Math.Pow(random.NextDouble(), 1 / shape);
        var d = shape - 1.0 / 3;
        var c = 1 / Math.Sqrt(9 * d);
        while (true)
        {
            double x, v;
            do
            {
                x = Normal(random);
                v = 1 + c * x;
            }
            while (v <= 0);

            v = v * v * v;
            var u = random.NextDouble();
            if (u < 1 - 0.0331 * x * x * x * x || Math.Log(u) < 0.5 * x * x + d * (1 - v + Math.Log(v))) return d * v;
        }
    }
}
