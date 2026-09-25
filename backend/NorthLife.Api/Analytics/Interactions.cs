namespace NorthLife.Api.Analytics;

/// <summary>What a visitor did with an event. Stored as a smallint.</summary>
public enum InteractionType : short
{
    Impression = 1,
    DetailView = 2,
    Navigate = 3,
    Share = 4,
}

/// <summary>Where the interaction happened. Feed positions feed the position-bias model.</summary>
public enum InteractionSource : short
{
    Feed = 1,
    Picks = 2,
    Map = 3,
    Details = 4,
    Direct = 5,
    Recommendations = 6,
    Similar = 7,
}

public static class InteractionWeights
{
    /// <summary>
    /// How much each interaction says about interest: seeing a card is weak evidence, opening it is
    /// stronger, and starting navigation is the strongest signal of intent to attend.
    /// </summary>
    public static double Base(InteractionType type) => type switch
    {
        InteractionType.Impression => 1,
        InteractionType.DetailView => 3,
        InteractionType.Navigate => 5,
        InteractionType.Share => 4,
        _ => 0,
    };
}

/// <summary>One row of the raw <c>interactions</c> table.</summary>
public sealed record RawInteraction(
    Guid EventId,
    Guid VisitorId,
    InteractionType Type,
    InteractionSource Source,
    short? Position,
    DateTimeOffset OccurredAtUtc);

public struct InteractionCounts
{
    public int Impressions;
    public int DetailViews;
    public int Navigations;
    public int Shares;

    public void Add(InteractionType type)
    {
        switch (type)
        {
            case InteractionType.Impression: Impressions++; break;
            case InteractionType.DetailView: DetailViews++; break;
            case InteractionType.Navigate: Navigations++; break;
            case InteractionType.Share: Shares++; break;
        }
    }
}

/// <summary>Calendar days as people in Israel see them; rollups and charts use these days.</summary>
public static class JerusalemDays
{
    private static readonly TimeZoneInfo Jerusalem = TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem");

    public static DateOnly Of(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Jerusalem).DateTime);

    /// <summary>UTC start (inclusive) of a Jerusalem day; handles the 23- and 25-hour DST days.</summary>
    public static DateTimeOffset StartUtc(DateOnly day) =>
        new(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), Jerusalem), TimeSpan.Zero);
}
