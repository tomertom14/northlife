using Microsoft.EntityFrameworkCore;
using NorthLife.Api.Contracts;
using NorthLife.Api.Data;
using NorthLife.Api.Images;
using NorthLife.Api.Models;

namespace NorthLife.Api.Services;

public sealed class AdminEventService(
    AppDbContext dbContext,
    EventLifecycleService lifecycle,
    EventImageService imageService)
{
    public async Task<IReadOnlyList<AdminEventResponse>> ListAsync(
        EventStatus? status,
        string? search,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Events.AsNoTracking()
            .Include(eventItem => eventItem.Owner)
            .Where(eventItem => eventItem.DeletedAtUtc == null);
        if (status is not null) query = query.Where(eventItem => eventItem.Status == status);

        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            if (term.Length > 100) throw new AdminEventValidationException("Search cannot exceed 100 characters.");
            var pattern = $"%{term}%";
            query = query.Where(eventItem =>
                EF.Functions.ILike(eventItem.Title, pattern) ||
                EF.Functions.ILike(eventItem.Locality, pattern) ||
                EF.Functions.ILike(eventItem.Owner.BusinessName, pattern));
        }

        // The pending queue is first-come first-served so older submissions never fall past the cap.
        var ordered = status == EventStatus.Pending
            ? query.OrderBy(eventItem => eventItem.UpdatedAtUtc)
            : query.OrderByDescending(eventItem => eventItem.UpdatedAtUtc);
        var events = await ordered
            .Take(200)
            .ToListAsync(cancellationToken);
        return events.Select(ToResponse).ToList();
    }

    public async Task<AdminEventResponse> CreateAsync(
        Guid adminId,
        OwnerEventUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var normalized = OwnerEventInputValidator.Validate(request, requireRevision: false);
        await imageService.RequireOwnedAsync(request.ImageId, adminId, true, cancellationToken);
        var admin = await dbContext.Users.SingleAsync(user => user.Id == adminId, cancellationToken);
        var eventItem = NewEvent(adminId, request, normalized);
        lifecycle.PublishNewByAdmin(eventItem);
        dbContext.Events.Add(eventItem);
        await dbContext.SaveChangesAsync(cancellationToken);
        eventItem.Owner = admin;
        return ToResponse(eventItem);
    }

    public async Task<AdminEventResponse> UpdateAsync(
        Guid id,
        OwnerEventUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var normalized = OwnerEventInputValidator.Validate(request, requireRevision: true);
        var eventItem = await RequireEventAsync(id, cancellationToken);
        await imageService.RequireOwnedAsync(request.ImageId, eventItem.OwnerId, true, cancellationToken);
        Apply(eventItem, request, normalized);
        lifecycle.EditByAdmin(eventItem, request.Revision!.Value);
        await SaveAsync(cancellationToken);
        return ToResponse(eventItem);
    }

    public Task<AdminEventResponse> ApproveAsync(Guid id, int revision, CancellationToken cancellationToken) =>
        ChangeAsync(id, eventItem => lifecycle.Approve(eventItem, revision), cancellationToken);

    public Task<AdminEventResponse> RejectAsync(Guid id, int revision, string reason, CancellationToken cancellationToken) =>
        ChangeAsync(id, eventItem => lifecycle.Reject(eventItem, revision, reason), cancellationToken);

    public Task<AdminEventResponse> HighlightAsync(Guid id, int revision, bool highlighted, CancellationToken cancellationToken) =>
        ChangeAsync(id, eventItem => lifecycle.SetHighlight(eventItem, revision, highlighted), cancellationToken);

    public async Task DeleteAsync(Guid id, int revision, CancellationToken cancellationToken)
    {
        var eventItem = await RequireEventAsync(id, cancellationToken);
        lifecycle.Delete(eventItem, revision);
        await SaveAsync(cancellationToken);
    }

    private async Task<AdminEventResponse> ChangeAsync(
        Guid id,
        Action<Event> change,
        CancellationToken cancellationToken)
    {
        var eventItem = await RequireEventAsync(id, cancellationToken);
        change(eventItem);
        await SaveAsync(cancellationToken);
        return ToResponse(eventItem);
    }

    private async Task<Event> RequireEventAsync(Guid id, CancellationToken cancellationToken) =>
        await dbContext.Events.Include(eventItem => eventItem.Owner)
            .SingleOrDefaultAsync(eventItem => eventItem.Id == id && eventItem.DeletedAtUtc == null, cancellationToken)
        ?? throw new AdminEventNotFoundException();

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new EventRevisionConflictException(0, 0); }
    }

    private static Event NewEvent(Guid ownerId, OwnerEventUpsertRequest request, NormalizedOwnerEventInput normalized)
    {
        var eventItem = new Event { OwnerId = ownerId, Title = "", Description = "", VenueName = "", Locality = "", Address = "", OrganizerName = "" };
        Apply(eventItem, request, normalized);
        return eventItem;
    }

    private static void Apply(Event eventItem, OwnerEventUpsertRequest request, NormalizedOwnerEventInput normalized)
    {
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
    }

    private static AdminEventResponse ToResponse(Event eventItem) => new(
        eventItem.Id, eventItem.OwnerId, eventItem.Owner.FullName, eventItem.Owner.BusinessName,
        eventItem.Title, eventItem.Description, eventItem.Category, eventItem.VenueName,
        eventItem.Locality, eventItem.Address, eventItem.Latitude, eventItem.Longitude,
        eventItem.StartAtUtc, eventItem.EndAtUtc, eventItem.Price, eventItem.ImageId,
        $"/api/images/{eventItem.ImageId}", eventItem.OrganizerName, eventItem.Tags,
        eventItem.Status, eventItem.IsHighlighted, eventItem.RejectionReason,
        eventItem.UpdatedAtUtc, eventItem.Revision);
}

public sealed class AdminEventNotFoundException : Exception;
public sealed class AdminEventValidationException(string message) : Exception(message);
