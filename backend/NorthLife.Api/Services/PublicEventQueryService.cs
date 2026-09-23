using Microsoft.EntityFrameworkCore;
using NorthLife.Api.Contracts;
using NorthLife.Api.Data;
using NorthLife.Api.Models;

namespace NorthLife.Api.Services;

public sealed class PublicEventQueryService(
    AppDbContext dbContext,
    TimeProvider timeProvider,
    EventTimeWindowFactory windowFactory)
{
    public async Task<PagedResponse<EventSummaryResponse>> GetPageAsync(
        PublicEventQueryParameters parameters,
        CancellationToken cancellationToken)
    {
        Validate(parameters);

        var now = timeProvider.GetUtcNow();
        var query = ApplySharedFilters(
            dbContext.Events
                .AsNoTracking()
                .Where(eventItem =>
                    eventItem.Status == EventStatus.Published &&
                    eventItem.EndAtUtc > now),
            parameters);

        query = ApplyPeriod(query, parameters, now);

        var totalCount = await query.CountAsync(cancellationToken);
        var skip = CalculateSkip(parameters.Page, parameters.PageSize);

        var rows = await query
            .OrderBy(eventItem => eventItem.StartAtUtc)
            .ThenBy(eventItem => eventItem.Id)
            .Skip(skip)
            .Take(parameters.PageSize)
            .Select(eventItem => new EventSummaryRow(
                eventItem.Id,
                eventItem.Title,
                eventItem.StartAtUtc,
                eventItem.EndAtUtc,
                eventItem.VenueName,
                eventItem.Locality,
                eventItem.Price,
                eventItem.Category,
                eventItem.ImageId,
                eventItem.IsHighlighted))
            .ToListAsync(cancellationToken);

        return new PagedResponse<EventSummaryResponse>(
            rows.Select(ToSummary).ToList(),
            parameters.Page,
            parameters.PageSize,
            totalCount);
    }

    public async Task<IReadOnlyList<EventSummaryResponse>> GetTopPicksAsync(
        PublicEventQueryParameters parameters,
        CancellationToken cancellationToken)
    {
        var topPickQuery = new PublicEventQueryParameters
        {
            Period = parameters.Period,
            From = parameters.From,
            To = parameters.To,
            Category = parameters.Category,
            Locality = parameters.Locality,
            MaxPrice = parameters.MaxPrice,
            Page = 1,
            PageSize = 10,
        };

        Validate(topPickQuery);
        var now = timeProvider.GetUtcNow();

        var query = ApplySharedFilters(
            dbContext.Events
                .AsNoTracking()
                .Where(eventItem =>
                    eventItem.Status == EventStatus.Published &&
                    eventItem.IsHighlighted &&
                    eventItem.EndAtUtc > now),
            topPickQuery);

        query = ApplyPeriod(query, topPickQuery, now);

        var rows = await query
            .OrderBy(eventItem => eventItem.StartAtUtc)
            .ThenBy(eventItem => eventItem.Id)
            .Take(10)
            .Select(eventItem => new EventSummaryRow(
                eventItem.Id,
                eventItem.Title,
                eventItem.StartAtUtc,
                eventItem.EndAtUtc,
                eventItem.VenueName,
                eventItem.Locality,
                eventItem.Price,
                eventItem.Category,
                eventItem.ImageId,
                eventItem.IsHighlighted))
            .ToListAsync(cancellationToken);

        return rows.Select(ToSummary).ToList();
    }

    public async Task<EventDetailsResponse?> GetDetailsAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var row = await dbContext.Events
            .AsNoTracking()
            .Where(eventItem =>
                eventItem.Id == id &&
                eventItem.Status == EventStatus.Published &&
                eventItem.EndAtUtc > now)
            .Select(eventItem => new EventDetailsRow(
                eventItem.Id,
                eventItem.Title,
                eventItem.Description,
                eventItem.Category,
                eventItem.VenueName,
                eventItem.Locality,
                eventItem.Address,
                eventItem.Latitude,
                eventItem.Longitude,
                eventItem.StartAtUtc,
                eventItem.EndAtUtc,
                eventItem.Price,
                eventItem.OrganizerName,
                eventItem.Tags,
                eventItem.ImageId,
                eventItem.IsHighlighted))
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new EventDetailsResponse(
                row.Id,
                row.Title,
                row.Description,
                row.Category,
                row.VenueName,
                row.Locality,
                row.Address,
                row.Latitude,
                row.Longitude,
                row.StartAtUtc,
                row.EndAtUtc,
                row.Price,
                row.OrganizerName,
                row.Tags,
                ImageUrl(row.ImageId),
                row.IsHighlighted);
    }

    public async Task<MapEventsResponse> GetMapAsync(
        MapEventQueryParameters map,
        CancellationToken cancellationToken)
    {
        ValidateBounds(map);
        var parameters = map.ToPublicQuery();
        Validate(parameters);
        var now = timeProvider.GetUtcNow();
        var query = ApplySharedFilters(
            dbContext.Events.AsNoTracking().Where(eventItem =>
                eventItem.Status == EventStatus.Published &&
                eventItem.EndAtUtc > now &&
                eventItem.Latitude >= map.South && eventItem.Latitude <= map.North &&
                eventItem.Longitude >= map.West && eventItem.Longitude <= map.East),
            parameters);
        query = ApplyPeriod(query, parameters, now);

        var rows = await query.OrderBy(eventItem => eventItem.StartAtUtc)
            .ThenBy(eventItem => eventItem.Id)
            .Take(201)
            .Select(eventItem => new MapEventResponse(
                eventItem.Id, eventItem.Title, eventItem.StartAtUtc,
                eventItem.VenueName, eventItem.Locality, eventItem.Address,
                eventItem.Latitude, eventItem.Longitude, eventItem.Category))
            .ToListAsync(cancellationToken);
        return new MapEventsResponse(rows.Take(200).ToList(), rows.Count > 200);
    }

    private IQueryable<Event> ApplyPeriod(
        IQueryable<Event> query,
        PublicEventQueryParameters parameters,
        DateTimeOffset now)
    {
        switch (parameters.Period.Trim().ToLowerInvariant())
        {
            case "now":
                return query.Where(eventItem =>
                    eventItem.StartAtUtc <= now &&
                    eventItem.EndAtUtc > now);
            case "today":
                return ApplyWindow(query, windowFactory.Today(now));
            case "tonight":
                return ApplyWindow(query, windowFactory.Tonight(now));
            case "tomorrow":
                return ApplyWindow(query, windowFactory.Tomorrow(now));
            case "range":
                if (parameters.From is null)
                {
                    throw new PublicEventQueryValidationException(
                        "from",
                        "A start date is required for a date-range query.");
                }

                if (parameters.To is null)
                {
                    throw new PublicEventQueryValidationException(
                        "to",
                        "An end date is required for a date-range query.");
                }

                return ApplyWindow(
                    query,
                    windowFactory.LocalDateRange(parameters.From.Value, parameters.To.Value));
            default:
                throw new PublicEventQueryValidationException(
                    "period",
                    "Period must be now, today, tonight, tomorrow, or range.");
        }
    }

    private static IQueryable<Event> ApplyWindow(
        IQueryable<Event> query,
        UtcEventWindow window) =>
        query.Where(eventItem =>
            eventItem.StartAtUtc < window.End &&
            eventItem.EndAtUtc > window.Start);

    private static IQueryable<Event> ApplySharedFilters(
        IQueryable<Event> query,
        PublicEventQueryParameters parameters)
    {
        if (parameters.Category is not null)
        {
            query = query.Where(eventItem => eventItem.Category == parameters.Category);
        }

        var locality = parameters.Locality?.Trim();
        if (!string.IsNullOrWhiteSpace(locality))
        {
            query = query.Where(eventItem => EF.Functions.ILike(eventItem.Locality, locality));
        }

        if (parameters.MaxPrice is not null)
        {
            query = query.Where(eventItem => eventItem.Price <= parameters.MaxPrice);
        }

        return query;
    }

    private static void Validate(PublicEventQueryParameters parameters)
    {
        if (parameters.Page < 1)
        {
            throw new PublicEventQueryValidationException(
                "page",
                "Page must be at least 1.");
        }

        if (parameters.PageSize is < 1 or > 100)
        {
            throw new PublicEventQueryValidationException(
                "pageSize",
                "Page size must be between 1 and 100.");
        }

        if (parameters.MaxPrice < 0)
        {
            throw new PublicEventQueryValidationException(
                "maxPrice",
                "Maximum price cannot be negative.");
        }

        if (parameters.Locality?.Trim().Length > 120)
        {
            throw new PublicEventQueryValidationException(
                "locality",
                "Locality cannot exceed 120 characters.");
        }
    }

    private static void ValidateBounds(MapEventQueryParameters map)
    {
        if (map.South is < -90 or > 90 || map.North is < -90 or > 90 || map.South >= map.North)
            throw new PublicEventQueryValidationException("bounds", "Latitude bounds are invalid.");
        if (map.West is < -180 or > 180 || map.East is < -180 or > 180 || map.West >= map.East)
            throw new PublicEventQueryValidationException("bounds", "Longitude bounds are invalid.");
    }

    private static int CalculateSkip(int page, int pageSize)
    {
        try
        {
            return checked((page - 1) * pageSize);
        }
        catch (OverflowException)
        {
            throw new PublicEventQueryValidationException(
                "page",
                "Page is too large.");
        }
    }

    private static EventSummaryResponse ToSummary(EventSummaryRow row) =>
        new(
            row.Id,
            row.Title,
            row.StartAtUtc,
            row.EndAtUtc,
            row.VenueName,
            row.Locality,
            row.Price,
            row.Category,
            ImageUrl(row.ImageId),
            row.IsHighlighted);

    private static string ImageUrl(Guid imageId) => $"/api/images/{imageId}";

    private sealed record EventSummaryRow(
        Guid Id,
        string Title,
        DateTimeOffset StartAtUtc,
        DateTimeOffset EndAtUtc,
        string VenueName,
        string Locality,
        decimal Price,
        EventCategory Category,
        Guid ImageId,
        bool IsHighlighted);

    private sealed record EventDetailsRow(
        Guid Id,
        string Title,
        string Description,
        EventCategory Category,
        string VenueName,
        string Locality,
        string Address,
        decimal Latitude,
        decimal Longitude,
        DateTimeOffset StartAtUtc,
        DateTimeOffset EndAtUtc,
        decimal Price,
        string OrganizerName,
        string[] Tags,
        Guid ImageId,
        bool IsHighlighted);
}
