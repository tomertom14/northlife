using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NorthLife.Api.Contracts;
using NorthLife.Api.Data;
using NorthLife.Api.Models;
using NorthLife.Api.Ranking;
using NorthLife.Api.Services;

namespace NorthLife.Api.Places;

/// <summary>
/// The public places directory. A place is public when it is published, not deleted and its owner is not suspended,
/// the same rule as for events. "Open now" is evaluated in SQL (<see cref="OpeningHours.OpenAt"/>), so it combines
/// with every other filter and with the exact geohash nearest-neighbour search reused from "near me" for events.
/// </summary>
public sealed class PlaceQueryService(
    AppDbContext dbContext,
    PublicEventQueryService events,
    IOptions<RankingOptions> ranking,
    TimeProvider timeProvider)
{
    private static readonly string[] Sorts = ["name", "near"];

    public async Task<PagedResponse<PlaceSummaryResponse>> GetPageAsync(PublicPlaceQueryParameters parameters, CancellationToken cancellationToken)
    {
        Validate(parameters);
        var now = OpeningHours.ToLocal(timeProvider.GetUtcNow());
        var query = Filtered(parameters, now);
        var total = await query.CountAsync(cancellationToken);
        var skip = (parameters.Page - 1) * parameters.PageSize;

        var location = parameters.Latitude is { } lat && parameters.Longitude is { } lon ? (lat, lon) : ((double, double)?)null;
        List<Guid> page;
        if (SortOf(parameters) == "near")
        {
            var (latitude, longitude) = location!.Value;
            var nearest = await NearestEvents.FindAsync<Guid>(
                latitude,
                longitude,
                skip + parameters.PageSize,
                ranking.Value.NearMaxRadiusKm,
                async prefixes =>
                {
                    var rows = await query
                        .Where(GeohashFilter.StartsWithAny<Place>(prefixes))
                        .Select(place => new { place.Id, place.Latitude, place.Longitude })
                        .ToListAsync(cancellationToken);
                    return rows.Select(row => new GeoCandidate<Guid>(row.Id, (double)row.Latitude, (double)row.Longitude)).ToList();
                });
            page = nearest.Items.Skip(skip).Take(parameters.PageSize).Select(item => item.Item).ToList();
        }
        else
        {
            page = await query.OrderBy(place => place.Name).ThenBy(place => place.Id)
                .Skip(skip).Take(parameters.PageSize)
                .Select(place => place.Id)
                .ToListAsync(cancellationToken);
        }

        var summaries = await SummariesAsync(page, now, location, cancellationToken);
        var items = page.Where(summaries.ContainsKey).Select(id => summaries[id]).ToList();
        return new PagedResponse<PlaceSummaryResponse>(items, parameters.Page, parameters.PageSize, total);
    }

    public async Task<PlaceDetailsResponse?> GetDetailsAsync(Guid id, CancellationToken cancellationToken)
    {
        var instant = timeProvider.GetUtcNow();
        var place = await Public()
            .Include(candidate => candidate.OpeningHours)
            .Include(candidate => candidate.Owner)
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (place is null) return null;

        var upcomingIds = await dbContext.Events.AsNoTracking()
            .Where(eventItem =>
                eventItem.PlaceId == id &&
                eventItem.Status == EventStatus.Published &&
                eventItem.Owner.SuspendedAtUtc == null &&
                eventItem.EndAtUtc > instant)
            .OrderBy(eventItem => eventItem.StartAtUtc)
            .ThenBy(eventItem => eventItem.Id)
            .Take(10)
            .Select(eventItem => eventItem.Id)
            .ToListAsync(cancellationToken);
        var summaries = await events.SummariesAsync(upcomingIds, cancellationToken);

        return new PlaceDetailsResponse(
            place.Id, place.Name, place.Category, place.Description, place.Locality, place.Address,
            place.Latitude, place.Longitude, place.Phone, place.Website, place.Instagram, place.StudentPerk,
            $"/api/images/{place.ImageId}", place.Owner.BusinessName,
            OwnerPlaceService.Hours(place.OpeningHours),
            OpenStatus(place.OpeningHours, OpeningHours.ToLocal(instant)),
            upcomingIds.Where(summaries.ContainsKey).Select(eventId => summaries[eventId]).ToList());
    }

    public async Task<MapPlacesResponse> GetMapAsync(MapPlaceQueryParameters map, CancellationToken cancellationToken)
    {
        if (map.South is < -90 or > 90 || map.North is < -90 or > 90 || map.South >= map.North)
            throw new PlaceQueryValidationException("bounds", "Latitude bounds are invalid.");
        if (map.West is < -180 or > 180 || map.East is < -180 or > 180 || map.West >= map.East)
            throw new PlaceQueryValidationException("bounds", "Longitude bounds are invalid.");

        var now = OpeningHours.ToLocal(timeProvider.GetUtcNow());
        var query = Public().Where(place =>
            place.Latitude >= map.South && place.Latitude <= map.North &&
            place.Longitude >= map.West && place.Longitude <= map.East);
        if (map.Category is not null) query = query.Where(place => place.Category == map.Category);
        if (map.OpenNow) query = query.Where(OpeningHours.OpenAt(now));

        var rows = await query.OrderBy(place => place.Name).ThenBy(place => place.Id)
            .Take(501)
            .Include(place => place.OpeningHours)
            .ToListAsync(cancellationToken);
        var items = rows.Take(500)
            .Select(place => new MapPlaceResponse(
                place.Id, place.Name, place.Category, place.Locality, place.Latitude, place.Longitude,
                OpeningHours.IsOpen(Intervals(place.OpeningHours), now)))
            .ToList();
        return new MapPlacesResponse(items, rows.Count > 500);
    }

    private IQueryable<Place> Public() =>
        dbContext.Places.AsNoTracking().Where(place =>
            place.Status == EventStatus.Published && place.Owner.SuspendedAtUtc == null);

    private IQueryable<Place> Filtered(PublicPlaceQueryParameters parameters, WeekTime now)
    {
        var query = Public();
        if (parameters.Category is not null) query = query.Where(place => place.Category == parameters.Category);

        var locality = parameters.Locality?.Trim();
        if (!string.IsNullOrEmpty(locality)) query = query.Where(place => EF.Functions.ILike(place.Locality, locality));

        var term = parameters.Q?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            var pattern = $"%{term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            query = query.Where(place =>
                EF.Functions.ILike(place.Name, pattern) ||
                EF.Functions.ILike(place.Description, pattern) ||
                EF.Functions.ILike(place.Locality, pattern));
        }

        if (parameters.OpenNow) query = query.Where(OpeningHours.OpenAt(now));
        return query;
    }

    private async Task<Dictionary<Guid, PlaceSummaryResponse>> SummariesAsync(
        IReadOnlyList<Guid> ids,
        WeekTime now,
        (double Latitude, double Longitude)? location,
        CancellationToken cancellationToken)
    {
        var wanted = ids.ToList();
        var places = await dbContext.Places.AsNoTracking()
            .Include(place => place.OpeningHours)
            .Where(place => wanted.Contains(place.Id))
            .ToListAsync(cancellationToken);
        return places.ToDictionary(
            place => place.Id,
            place => new PlaceSummaryResponse(
                place.Id, place.Name, place.Category, place.Locality, $"/api/images/{place.ImageId}",
                place.StudentPerk, OpenStatus(place.OpeningHours, now),
                location is { } visitor
                    ? Math.Round(Haversine.DistanceKm(visitor.Latitude, visitor.Longitude, (double)place.Latitude, (double)place.Longitude), 2)
                    : null));
    }

    internal static PlaceOpenStatusResponse OpenStatus(IEnumerable<PlaceOpeningHours> rows, WeekTime now)
    {
        var intervals = Intervals(rows);
        if (intervals.Count == 0) return new PlaceOpenStatusResponse(false, false, false, null, null, null, null);
        var status = OpeningHours.Status(intervals, now);
        return new PlaceOpenStatusResponse(
            true, status.IsOpen, status.AlwaysOpen,
            status.Next?.Kind, status.Next?.Day, status.Next?.Minute, status.Next?.DaysAhead);
    }

    private static List<HoursInterval> Intervals(IEnumerable<PlaceOpeningHours> rows) =>
        rows.Select(row => new HoursInterval(row.DayOfWeek, row.OpensMinute, row.ClosesMinute)).ToList();

    private static string SortOf(PublicPlaceQueryParameters parameters) =>
        string.IsNullOrWhiteSpace(parameters.Sort) ? "name" : parameters.Sort.Trim().ToLowerInvariant();

    private static void Validate(PublicPlaceQueryParameters parameters)
    {
        if (parameters.Page is < 1 or > 10_000) throw new PlaceQueryValidationException("page", "Page must be between 1 and 10000.");
        if (parameters.PageSize is < 1 or > 50) throw new PlaceQueryValidationException("pageSize", "Page size must be between 1 and 50.");
        if (!Sorts.Contains(SortOf(parameters))) throw new PlaceQueryValidationException("sort", "Sort must be name or near.");
        if (parameters.Category is { } category && !Enum.IsDefined(category)) throw new PlaceQueryValidationException("category", "Unknown category.");
        if (parameters.Locality?.Trim().Length > 120) throw new PlaceQueryValidationException("locality", "Locality cannot exceed 120 characters.");
        if (parameters.Q?.Trim().Length > 100) throw new PlaceQueryValidationException("q", "Search cannot exceed 100 characters.");
        if ((parameters.Latitude is null) != (parameters.Longitude is null))
            throw new PlaceQueryValidationException("latitude", "Send both latitude and longitude, or neither.");
        if (parameters.Latitude is < -90 or > 90) throw new PlaceQueryValidationException("latitude", "Latitude must be between -90 and 90.");
        if (parameters.Longitude is < -180 or > 180) throw new PlaceQueryValidationException("longitude", "Longitude must be between -180 and 180.");
        if (SortOf(parameters) == "near" && parameters.Latitude is null)
            throw new PlaceQueryValidationException("latitude", "Sorting by distance needs the visitor's location.");
    }
}

public sealed class PlaceQueryValidationException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}
