using NorthLife.Api.Models;

namespace NorthLife.Api.Contracts;

public sealed record EventSummaryResponse(
    Guid Id,
    string Title,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string VenueName,
    string Locality,
    decimal Price,
    EventCategory Category,
    string ImageUrl,
    bool IsHighlighted);

public sealed record EventDetailsResponse(
    Guid Id,
    string Title,
    string Description,
    EventCategory Category,
    string VenueName,
    string Locality,
    string Address,
    decimal Latitude,
    decimal Longitude,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    decimal Price,
    string OrganizerName,
    IReadOnlyList<string> Tags,
    string ImageUrl,
    bool IsHighlighted);
