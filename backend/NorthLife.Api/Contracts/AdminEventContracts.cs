using NorthLife.Api.Models;

namespace NorthLife.Api.Contracts;

public sealed record AdminEventResponse(
    Guid Id,
    Guid OwnerId,
    string OwnerName,
    string OwnerBusinessName,
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
    Guid ImageId,
    string ImageUrl,
    string OrganizerName,
    IReadOnlyList<string> Tags,
    EventStatus Status,
    bool IsHighlighted,
    string? RejectionReason,
    DateTimeOffset UpdatedAt,
    int Revision);

public sealed record RevisionRequest(int Revision);
public sealed record RejectEventRequest(int Revision, string Reason);
public sealed record HighlightEventRequest(int Revision, bool IsHighlighted);
