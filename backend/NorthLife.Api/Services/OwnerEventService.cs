using Microsoft.EntityFrameworkCore;
using NorthLife.Api.Contracts;
using NorthLife.Api.Data;
using NorthLife.Api.Images;
using NorthLife.Api.Models;

namespace NorthLife.Api.Services;

public sealed class OwnerEventService(
    AppDbContext dbContext,
    EventLifecycleService lifecycle,
    EventImageService imageService)
{
    public async Task<IReadOnlyList<OwnerEventResponse>> ListAsync(
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        var events = await dbContext.Events
            .AsNoTracking()
            .Where(eventItem => eventItem.OwnerId == ownerId)
            .OrderByDescending(eventItem => eventItem.UpdatedAtUtc)
            .ToListAsync(cancellationToken);
        return events.Select(ToResponse).ToList();
    }

    public async Task<OwnerEventResponse?> GetAsync(
        Guid ownerId,
        Guid id,
        CancellationToken cancellationToken)
    {
        var eventItem = await dbContext.Events
            .AsNoTracking()
            .Where(candidate => candidate.OwnerId == ownerId && candidate.Id == id)
            .SingleOrDefaultAsync(cancellationToken);
        return eventItem is null ? null : ToResponse(eventItem);
    }

    public async Task<OwnerEventResponse> CreateAsync(
        Guid ownerId,
        OwnerEventUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var normalized = OwnerEventInputValidator.Validate(request, requireRevision: false);
        await imageService.RequireOwnedAsync(
            request.ImageId,
            ownerId,
            isAdmin: false,
            cancellationToken);

        var eventItem = new Event
        {
            OwnerId = ownerId,
            Title = normalized.Title,
            Description = normalized.Description,
            Category = request.Category,
            VenueName = normalized.VenueName,
            Locality = normalized.Locality,
            Address = normalized.Address,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            StartAtUtc = request.StartAt,
            EndAtUtc = request.EndAt,
            Price = request.Price,
            ImageId = request.ImageId,
            OrganizerName = normalized.OrganizerName,
            Tags = normalized.Tags,
        };
        lifecycle.SubmitNewByOwner(eventItem);
        dbContext.Events.Add(eventItem);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(eventItem);
    }

    public async Task<OwnerEventResponse> UpdateAsync(
        Guid ownerId,
        Guid id,
        OwnerEventUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var normalized = OwnerEventInputValidator.Validate(request, requireRevision: true);
        var eventItem = await RequireOwnedEventAsync(ownerId, id, cancellationToken);
        await imageService.RequireOwnedAsync(
            request.ImageId,
            ownerId,
            isAdmin: false,
            cancellationToken);

        eventItem.Title = normalized.Title;
        eventItem.Description = normalized.Description;
        eventItem.Category = request.Category;
        eventItem.VenueName = normalized.VenueName;
        eventItem.Locality = normalized.Locality;
        eventItem.Address = normalized.Address;
        eventItem.Latitude = request.Latitude;
        eventItem.Longitude = request.Longitude;
        eventItem.StartAtUtc = request.StartAt;
        eventItem.EndAtUtc = request.EndAt;
        eventItem.Price = request.Price;
        eventItem.ImageId = request.ImageId;
        eventItem.OrganizerName = normalized.OrganizerName;
        eventItem.Tags = normalized.Tags;
        lifecycle.ResubmitOwnerEdit(eventItem, request.Revision!.Value);

        await SaveWithConcurrencyAsync(cancellationToken);
        return ToResponse(eventItem);
    }

    public async Task DeleteAsync(
        Guid ownerId,
        Guid id,
        int expectedRevision,
        CancellationToken cancellationToken)
    {
        var eventItem = await RequireOwnedEventAsync(ownerId, id, cancellationToken);
        lifecycle.Delete(eventItem, expectedRevision);
        await SaveWithConcurrencyAsync(cancellationToken);
    }

    private async Task<Event> RequireOwnedEventAsync(
        Guid ownerId,
        Guid id,
        CancellationToken cancellationToken) =>
        await dbContext.Events.SingleOrDefaultAsync(
            eventItem => eventItem.Id == id && eventItem.OwnerId == ownerId,
            cancellationToken)
        ?? throw new OwnerEventNotFoundException();

    private async Task SaveWithConcurrencyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new EventRevisionConflictException(0, 0);
        }
    }

    private static OwnerEventResponse ToResponse(Event eventItem) =>
        new(
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
            eventItem.ImageId,
            $"/api/images/{eventItem.ImageId}",
            eventItem.OrganizerName,
            eventItem.Tags,
            eventItem.Status,
            eventItem.RejectionReason,
            eventItem.UpdatedAtUtc,
            eventItem.Revision);
}

public static class OwnerEventInputValidator
{
    public static NormalizedOwnerEventInput Validate(
        OwnerEventUpsertRequest request,
        bool requireRevision)
    {
        var errors = new Dictionary<string, string[]>();
        var title = Required(request.Title, 150, "title", errors);
        var description = Required(request.Description, 5000, "description", errors);
        var venue = Required(request.VenueName, 200, "venueName", errors);
        var locality = Required(request.Locality, 120, "locality", errors);
        var address = Required(request.Address, 300, "address", errors);
        var organizer = Required(request.OrganizerName, 200, "organizerName", errors);

        if (request.ImageId == Guid.Empty) errors["imageId"] = ["Image is required."];
        if (request.Price < 0) errors["price"] = ["Price cannot be negative."];
        if (request.Latitude is < -90 or > 90) errors["latitude"] = ["Latitude must be between -90 and 90."];
        if (request.Longitude is < -180 or > 180) errors["longitude"] = ["Longitude must be between -180 and 180."];
        if (request.EndAt <= request.StartAt) errors["endAt"] = ["End time must follow start time."];
        if (requireRevision && request.Revision is null or < 1) errors["revision"] = ["A valid revision is required."];

        var tags = (request.Tags ?? [])
            .Select(tag => tag?.Trim() ?? string.Empty)
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (tags.Length > 10 || tags.Any(tag => tag.Length > 30))
        {
            errors["tags"] = ["Use at most 10 tags of up to 30 characters each."];
        }

        if (errors.Count > 0) throw new OwnerEventValidationException(errors);
        return new NormalizedOwnerEventInput(
            title, description, venue, locality, address, organizer, tags);
    }

    private static string Required(
        string? value,
        int maximum,
        string field,
        Dictionary<string, string[]> errors)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is 0 || normalized.Length > maximum)
        {
            errors[field] = [$"Field is required and cannot exceed {maximum} characters."];
        }
        return normalized;
    }
}

public sealed record NormalizedOwnerEventInput(
    string Title,
    string Description,
    string VenueName,
    string Locality,
    string Address,
    string OrganizerName,
    string[] Tags);

public sealed class OwnerEventValidationException(
    IReadOnlyDictionary<string, string[]> errors) : Exception
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class OwnerEventNotFoundException : Exception;
