using NorthLife.Api.Models;

namespace NorthLife.Api.Contracts;

/// <summary>One opening interval: day of week (0 = Sunday) and minutes from midnight, Israel local time.</summary>
public sealed record PlaceHoursDto(int Day, int Opens, int Closes);

public sealed record OwnerPlaceUpsertRequest(
    string? Name,
    PlaceCategory Category,
    string? Description,
    string? Locality,
    string? Address,
    decimal Latitude,
    decimal Longitude,
    string? Phone,
    string? Website,
    string? Instagram,
    string? StudentPerk,
    Guid ImageId,
    IReadOnlyList<PlaceHoursDto>? Hours,
    int? Revision);

public sealed record OwnerPlaceResponse(
    Guid Id,
    string Name,
    PlaceCategory Category,
    string Description,
    string Locality,
    string Address,
    decimal Latitude,
    decimal Longitude,
    string? Phone,
    string? Website,
    string? Instagram,
    string? StudentPerk,
    Guid ImageId,
    string ImageUrl,
    IReadOnlyList<PlaceHoursDto> Hours,
    EventStatus Status,
    string? RejectionReason,
    DateTimeOffset UpdatedAt,
    int Revision);

/// <summary>Open status at request time; the next change is a local day of week and minute, plus days ahead.</summary>
public sealed record PlaceOpenStatusResponse(
    bool HasHours,
    bool IsOpen,
    bool AlwaysOpen,
    string? NextKind,
    int? NextDay,
    int? NextMinute,
    int? NextDaysAhead);

public sealed record PlaceSummaryResponse(
    Guid Id,
    string Name,
    PlaceCategory Category,
    string Locality,
    string ImageUrl,
    string? StudentPerk,
    PlaceOpenStatusResponse Open,
    double? DistanceKm = null);

public sealed record PlaceDetailsResponse(
    Guid Id,
    string Name,
    PlaceCategory Category,
    string Description,
    string Locality,
    string Address,
    decimal Latitude,
    decimal Longitude,
    string? Phone,
    string? Website,
    string? Instagram,
    string? StudentPerk,
    string ImageUrl,
    string BusinessName,
    IReadOnlyList<PlaceHoursDto> Hours,
    PlaceOpenStatusResponse Open,
    IReadOnlyList<EventSummaryResponse> UpcomingEvents);

public sealed record MapPlaceResponse(
    Guid Id,
    string Name,
    PlaceCategory Category,
    string Locality,
    decimal Latitude,
    decimal Longitude,
    bool IsOpen);

public sealed record MapPlacesResponse(IReadOnlyList<MapPlaceResponse> Items, bool Truncated);

/// <summary>The place an event happens at, shown on the event page when the place is public.</summary>
public sealed record EventPlaceLink(Guid Id, string Name);

public sealed record AdminPlaceResponse(
    Guid Id,
    Guid OwnerId,
    string OwnerName,
    string BusinessName,
    string Name,
    PlaceCategory Category,
    string Description,
    string Locality,
    string Address,
    decimal Latitude,
    decimal Longitude,
    string? Phone,
    string? Website,
    string? Instagram,
    string? StudentPerk,
    string ImageUrl,
    IReadOnlyList<PlaceHoursDto> Hours,
    EventStatus Status,
    string? RejectionReason,
    DateTimeOffset UpdatedAt,
    int Revision);

public sealed record AdminPlaceRejectRequest(int Revision, string? Reason);

public sealed record AdminPlaceRevisionRequest(int Revision);

public sealed class PublicPlaceQueryParameters
{
    public PlaceCategory? Category { get; init; }
    public string? Locality { get; init; }
    public bool OpenNow { get; init; }
    public string? Q { get; init; }
    public string? Sort { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 12;
}

public sealed class MapPlaceQueryParameters
{
    public decimal North { get; init; } = 33.4m;
    public decimal South { get; init; } = 32.6m;
    public decimal East { get; init; } = 36m;
    public decimal West { get; init; } = 34.8m;
    public PlaceCategory? Category { get; init; }
    public bool OpenNow { get; init; }
}
