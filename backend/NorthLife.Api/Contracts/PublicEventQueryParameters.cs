using NorthLife.Api.Models;

namespace NorthLife.Api.Contracts;

public sealed class PublicEventQueryParameters
{
    public string Period { get; init; } = "today";
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public EventCategory? Category { get; init; }
    public string? Locality { get; init; }
    public decimal? MaxPrice { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;

    /// <summary>"time" (default), "hot" or "near".</summary>
    public string? Sort { get; init; }

    /// <summary>Visitor location for "near" and for the distance term of "hot"; never stored.</summary>
    public double? Latitude { get; init; }

    public double? Longitude { get; init; }
}

public sealed class EventFilterParameters
{
    public EventCategory? Category { get; init; }
    public string? Locality { get; init; }
    public decimal? MaxPrice { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;

    public PublicEventQueryParameters WithPeriod(
        string period,
        DateOnly? from = null,
        DateOnly? to = null) =>
        new()
        {
            Period = period,
            From = from,
            To = to,
            Category = Category,
            Locality = Locality,
            MaxPrice = MaxPrice,
            Page = Page,
            PageSize = PageSize,
        };
}

public sealed class MapEventQueryParameters
{
    public decimal North { get; init; } = 33.4m;
    public decimal South { get; init; } = 32.6m;
    public decimal East { get; init; } = 36m;
    public decimal West { get; init; } = 34.8m;
    public string Period { get; init; } = "today";
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public EventCategory? Category { get; init; }
    public string? Locality { get; init; }
    public decimal? MaxPrice { get; init; }

    public PublicEventQueryParameters ToPublicQuery() => new()
    {
        Period = Period,
        From = From,
        To = To,
        Category = Category,
        Locality = Locality,
        MaxPrice = MaxPrice,
        Page = 1,
        PageSize = 100,
    };
}
