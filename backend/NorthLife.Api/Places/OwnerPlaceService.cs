using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NorthLife.Api.Contracts;
using NorthLife.Api.Data;
using NorthLife.Api.Images;
using NorthLife.Api.Models;
using NorthLife.Api.Services;

namespace NorthLife.Api.Places;

/// <summary>A business owner's own places: list, create, edit (back to review) and delete.</summary>
public sealed class OwnerPlaceService(
    AppDbContext dbContext,
    PlaceLifecycle lifecycle,
    EventImageService imageService)
{
    public async Task<IReadOnlyList<OwnerPlaceResponse>> ListAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var places = await dbContext.Places
            .AsNoTracking()
            .Include(place => place.OpeningHours)
            .Where(place => place.OwnerId == ownerId)
            .OrderByDescending(place => place.UpdatedAtUtc)
            .ToListAsync(cancellationToken);
        return places.Select(ToResponse).ToList();
    }

    public async Task<OwnerPlaceResponse?> GetAsync(Guid ownerId, Guid id, CancellationToken cancellationToken)
    {
        var place = await dbContext.Places
            .AsNoTracking()
            .Include(candidate => candidate.OpeningHours)
            .SingleOrDefaultAsync(candidate => candidate.OwnerId == ownerId && candidate.Id == id, cancellationToken);
        return place is null ? null : ToResponse(place);
    }

    public async Task<OwnerPlaceResponse> CreateAsync(Guid ownerId, OwnerPlaceUpsertRequest request, CancellationToken cancellationToken)
    {
        var input = PlaceInputValidator.Validate(request, requireRevision: false);
        await RequireConfirmedEmailAsync(ownerId, cancellationToken);
        await imageService.RequireOwnedAsync(request.ImageId, ownerId, isAdmin: false, cancellationToken);

        var place = new Place
        {
            OwnerId = ownerId,
            Name = input.Name,
            Description = input.Description,
            Locality = input.Locality,
            Address = input.Address,
        };
        Apply(place, request, input);
        lifecycle.SubmitNew(place);
        dbContext.Places.Add(place);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(place);
    }

    public async Task<OwnerPlaceResponse> UpdateAsync(
        Guid ownerId,
        Guid id,
        OwnerPlaceUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var input = PlaceInputValidator.Validate(request, requireRevision: true);
        await RequireConfirmedEmailAsync(ownerId, cancellationToken);
        var place = await RequireOwnedAsync(ownerId, id, cancellationToken);
        await imageService.RequireOwnedAsync(request.ImageId, ownerId, isAdmin: false, cancellationToken);

        lifecycle.ResubmitOwnerEdit(place, request.Revision!.Value);
        Apply(place, request, input);
        await SaveAsync(cancellationToken);
        return ToResponse(place);
    }

    public async Task DeleteAsync(Guid ownerId, Guid id, int expectedRevision, CancellationToken cancellationToken)
    {
        var place = await RequireOwnedAsync(ownerId, id, cancellationToken);
        lifecycle.Delete(place, expectedRevision);
        await SaveAsync(cancellationToken);
    }

    internal static void Apply(Place place, OwnerPlaceUpsertRequest request, PlaceInput input)
    {
        place.Name = input.Name;
        place.Category = request.Category;
        place.Description = input.Description;
        place.Locality = input.Locality;
        place.Address = input.Address;
        place.Latitude = request.Latitude;
        place.Longitude = request.Longitude;
        place.Phone = input.Phone;
        place.Website = input.Website;
        place.Instagram = input.Instagram;
        place.StudentPerk = input.StudentPerk;
        place.ImageId = request.ImageId;
        SyncHours(place, input.Hours);
    }

    /// <summary>
    /// Brings the tracked hour rows in line with the requested ones: rows keyed by (day, opening minute) are kept and
    /// updated, missing ones are removed (orphans of a required relationship are deleted) and new ones added.
    /// Deleting and re-adding the same key in one save would clash in the change tracker.
    /// </summary>
    private static void SyncHours(Place place, IReadOnlyList<HoursInterval> hours)
    {
        var wanted = hours.ToDictionary(interval => ((short)interval.Day, (short)interval.Opens), interval => (short)interval.Closes);
        foreach (var row in place.OpeningHours.ToList())
        {
            var key = (row.DayOfWeek, row.OpensMinute);
            if (wanted.Remove(key, out var closes))
            {
                row.ClosesMinute = closes;
            }
            else
            {
                place.OpeningHours.Remove(row);
            }
        }

        foreach (var ((day, opens), closes) in wanted)
        {
            place.OpeningHours.Add(new PlaceOpeningHours { PlaceId = place.Id, DayOfWeek = day, OpensMinute = opens, ClosesMinute = closes });
        }
    }

    internal static IReadOnlyList<PlaceHoursDto> Hours(IEnumerable<PlaceOpeningHours> hours) =>
        hours.OrderBy(row => row.DayOfWeek).ThenBy(row => row.OpensMinute)
            .Select(row => new PlaceHoursDto(row.DayOfWeek, row.OpensMinute, row.ClosesMinute))
            .ToList();

    private async Task RequireConfirmedEmailAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        if (!await dbContext.Users.AnyAsync(user => user.Id == ownerId && user.EmailConfirmedAtUtc != null, cancellationToken))
        {
            throw new EmailNotConfirmedException();
        }
    }

    private async Task<Place> RequireOwnedAsync(Guid ownerId, Guid id, CancellationToken cancellationToken) =>
        await dbContext.Places
            .Include(place => place.OpeningHours)
            .SingleOrDefaultAsync(place => place.Id == id && place.OwnerId == ownerId, cancellationToken)
        ?? throw new PlaceNotFoundException();

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new PlaceRevisionConflictException(); }
    }

    private static OwnerPlaceResponse ToResponse(Place place) => new(
        place.Id, place.Name, place.Category, place.Description, place.Locality, place.Address,
        place.Latitude, place.Longitude, place.Phone, place.Website, place.Instagram, place.StudentPerk,
        place.ImageId, $"/api/images/{place.ImageId}", Hours(place.OpeningHours),
        place.Status, place.RejectionReason, place.UpdatedAtUtc, place.Revision);
}

public sealed record PlaceInput(
    string Name,
    string Description,
    string Locality,
    string Address,
    string? Phone,
    string? Website,
    string? Instagram,
    string? StudentPerk,
    IReadOnlyList<HoursInterval> Hours);

public static partial class PlaceInputValidator
{
    public static PlaceInput Validate(OwnerPlaceUpsertRequest request, bool requireRevision)
    {
        var errors = new Dictionary<string, string[]>();
        var name = Required(request.Name, 150, "name", errors);
        var description = Required(request.Description, 3000, "description", errors);
        var locality = Required(request.Locality, 120, "locality", errors);
        var address = Required(request.Address, 300, "address", errors);

        if (!Enum.IsDefined(request.Category)) errors["category"] = ["Unknown category."];
        if (request.ImageId == Guid.Empty) errors["imageId"] = ["Image is required."];
        if (request.Latitude is < -90 or > 90) errors["latitude"] = ["Latitude must be between -90 and 90."];
        if (request.Longitude is < -180 or > 180) errors["longitude"] = ["Longitude must be between -180 and 180."];
        if (requireRevision && request.Revision is null or < 1) errors["revision"] = ["A valid revision is required."];

        var phone = Optional(request.Phone);
        if (phone is not null && !PhonePattern().IsMatch(phone)) errors["phone"] = ["Use digits, spaces, dashes or a leading +, 7 to 30 characters."];

        var website = Optional(request.Website);
        if (website is not null &&
            (website.Length > 300 ||
             !Uri.TryCreate(website, UriKind.Absolute, out var uri) ||
             uri.Scheme is not ("http" or "https")))
        {
            errors["website"] = ["Use a full http:// or https:// address of up to 300 characters."];
        }

        var instagram = Optional(request.Instagram)?.TrimStart('@');
        if (instagram is not null && !InstagramPattern().IsMatch(instagram)) errors["instagram"] = ["Use an Instagram handle of up to 30 letters, digits, dots or underscores."];

        var perk = Optional(request.StudentPerk);
        if (perk?.Length > 200) errors["studentPerk"] = ["A student perk cannot exceed 200 characters."];

        var hours = (request.Hours ?? []).Select(dto => new HoursInterval(dto.Day, dto.Opens, dto.Closes)).ToList();
        var hourErrors = OpeningHours.Validate(hours);
        if (hourErrors.Count > 0) errors["hours"] = [.. hourErrors];
        if (hours.Count > 7 * OpeningHours.MaxIntervalsPerDay) errors["hours"] = ["Too many opening intervals."];
        if (hours.Distinct().Count() != hours.Count) errors["hours"] = ["An opening interval is listed twice."];

        if (errors.Count > 0) throw new PlaceValidationException(errors);
        return new PlaceInput(name, description, locality, address, phone, website, instagram, perk, hours);
    }

    private static string Required(string? value, int maximum, string field, Dictionary<string, string[]> errors)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is 0 || normalized.Length > maximum)
        {
            errors[field] = [$"Field is required and cannot exceed {maximum} characters."];
        }

        return normalized;
    }

    private static string? Optional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    [GeneratedRegex(@"^\+?[0-9][0-9\s\-]{6,29}$")]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"^[A-Za-z0-9._]{1,30}$")]
    private static partial Regex InstagramPattern();
}
