using Microsoft.EntityFrameworkCore;
using NorthLife.Api.Contracts;
using NorthLife.Api.Data;
using NorthLife.Api.Models;
using NorthLife.Api.Services;

namespace NorthLife.Api.Places;

/// <summary>Moderation of places by administrators: the review queue, approval, rejection with a reason, deletion.</summary>
public sealed class AdminPlaceService(AppDbContext dbContext, PlaceLifecycle lifecycle, AuditLog audit)
{
    public async Task<IReadOnlyList<AdminPlaceResponse>> ListAsync(EventStatus? status, string? search, CancellationToken cancellationToken)
    {
        var query = dbContext.Places.AsNoTracking()
            .Include(place => place.Owner)
            .Include(place => place.OpeningHours)
            .AsQueryable();
        if (status is not null) query = query.Where(place => place.Status == status);

        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            if (term.Length > 100) throw new PlaceValidationException(new Dictionary<string, string[]> { ["search"] = ["Search cannot exceed 100 characters."] });
            var pattern = $"%{term}%";
            query = query.Where(place =>
                EF.Functions.ILike(place.Name, pattern) ||
                EF.Functions.ILike(place.Locality, pattern) ||
                EF.Functions.ILike(place.Owner.BusinessName, pattern));
        }

        // First come, first served for the review queue; newest first otherwise.
        var ordered = status == EventStatus.Pending
            ? query.OrderBy(place => place.UpdatedAtUtc)
            : query.OrderByDescending(place => place.UpdatedAtUtc);
        var places = await ordered.Take(200).ToListAsync(cancellationToken);
        return places.Select(ToResponse).ToList();
    }

    public Task<AdminPlaceResponse> ApproveAsync(Guid actorId, Guid id, int revision, CancellationToken cancellationToken) =>
        ChangeAsync(actorId, id, "place.approved", place => lifecycle.Approve(place, revision), cancellationToken);

    public Task<AdminPlaceResponse> RejectAsync(Guid actorId, Guid id, int revision, string? reason, CancellationToken cancellationToken) =>
        ChangeAsync(actorId, id, "place.rejected", place => lifecycle.Reject(place, revision, reason), cancellationToken);

    public async Task DeleteAsync(Guid actorId, Guid id, int revision, CancellationToken cancellationToken)
    {
        var place = await RequireAsync(id, cancellationToken);
        lifecycle.Delete(place, revision);
        audit.Record(actorId, "place.deleted", "Place", place.Id, new { place.Name });
        await SaveAsync(cancellationToken);
    }

    private async Task<AdminPlaceResponse> ChangeAsync(
        Guid actorId,
        Guid id,
        string action,
        Action<Place> change,
        CancellationToken cancellationToken)
    {
        var place = await RequireAsync(id, cancellationToken);
        var before = place.Status;
        change(place);
        audit.Record(actorId, action, "Place", place.Id, new
        {
            place.Name,
            from = before.ToString(),
            to = place.Status.ToString(),
            place.RejectionReason,
        });
        await SaveAsync(cancellationToken);
        return ToResponse(place);
    }

    private async Task<Place> RequireAsync(Guid id, CancellationToken cancellationToken) =>
        await dbContext.Places
            .Include(place => place.Owner)
            .Include(place => place.OpeningHours)
            .SingleOrDefaultAsync(place => place.Id == id, cancellationToken)
        ?? throw new PlaceNotFoundException();

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new PlaceRevisionConflictException(); }
    }

    private static AdminPlaceResponse ToResponse(Place place) => new(
        place.Id, place.OwnerId, place.Owner.FullName, place.Owner.BusinessName,
        place.Name, place.Category, place.Description, place.Locality, place.Address,
        place.Latitude, place.Longitude, place.Phone, place.Website, place.Instagram, place.StudentPerk,
        $"/api/images/{place.ImageId}", OwnerPlaceService.Hours(place.OpeningHours),
        place.Status, place.RejectionReason, place.UpdatedAtUtc, place.Revision);
}
