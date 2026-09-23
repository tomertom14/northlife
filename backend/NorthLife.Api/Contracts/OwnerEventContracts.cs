using NorthLife.Api.Models;

namespace NorthLife.Api.Contracts;

public sealed record OwnerEventUpsertRequest(
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
    string OrganizerName,
    IReadOnlyList<string> Tags,
    int? Revision);

public sealed record OwnerEventResponse(
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
    Guid ImageId,
    string ImageUrl,
    string OrganizerName,
    IReadOnlyList<string> Tags,
    EventStatus Status,
    string? RejectionReason,
    DateTimeOffset UpdatedAt,
    int Revision);
