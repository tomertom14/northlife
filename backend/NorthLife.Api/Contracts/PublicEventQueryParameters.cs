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
