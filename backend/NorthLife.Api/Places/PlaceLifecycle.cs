using NorthLife.Api.Models;

namespace NorthLife.Api.Places;

/// <summary>
/// Status changes of a place, mirroring events: owners submit, an administrator approves or rejects with a reason,
/// an owner edit sends a published place back to review, and deletion is soft. Every change checks the revision the
/// caller saw and bumps it, so two admins or an admin and the owner cannot overwrite each other.
/// </summary>
public sealed class PlaceLifecycle(TimeProvider timeProvider)
{
    public void SubmitNew(Place place)
    {
        var now = timeProvider.GetUtcNow();
        place.Status = EventStatus.Pending;
        place.RejectionReason = null;
        place.DeletedAtUtc = null;
        place.CreatedAtUtc = now;
        place.UpdatedAtUtc = now;
        place.Revision = 1;
    }

    public void ResubmitOwnerEdit(Place place, int expectedRevision)
    {
        EnsureRevision(place, expectedRevision);
        EnsureAvailable(place);
        place.Status = EventStatus.Pending;
        place.RejectionReason = null;
        Touch(place);
    }

    public void Approve(Place place, int expectedRevision)
    {
        EnsureRevision(place, expectedRevision);
        EnsureAvailable(place);
        EnsureStatus(place, EventStatus.Pending);
        place.Status = EventStatus.Published;
        place.RejectionReason = null;
        Touch(place);
    }

    public void Reject(Place place, int expectedRevision, string? reason)
    {
        EnsureRevision(place, expectedRevision);
        EnsureAvailable(place);
        EnsureStatus(place, EventStatus.Pending);
        var normalized = reason?.Trim() ?? string.Empty;
        if (normalized.Length is 0 or > 1000)
        {
            throw new PlaceLifecycleException("A rejection reason of 1 to 1000 characters is required.");
        }

        place.Status = EventStatus.Rejected;
        place.RejectionReason = normalized;
        Touch(place);
    }

    public void Delete(Place place, int expectedRevision)
    {
        EnsureRevision(place, expectedRevision);
        EnsureAvailable(place);
        place.DeletedAtUtc = timeProvider.GetUtcNow();
        Touch(place);
    }

    private static void EnsureRevision(Place place, int expectedRevision)
    {
        if (place.Revision != expectedRevision) throw new PlaceRevisionConflictException();
    }

    private static void EnsureAvailable(Place place)
    {
        if (place.DeletedAtUtc is not null) throw new PlaceLifecycleException("A deleted place cannot change state.");
    }

    private static void EnsureStatus(Place place, EventStatus expected)
    {
        if (place.Status != expected) throw new PlaceLifecycleException($"Place must be {expected} for this operation.");
    }

    private void Touch(Place place)
    {
        place.UpdatedAtUtc = timeProvider.GetUtcNow();
        place.Revision++;
    }
}

public sealed class PlaceLifecycleException(string message) : Exception(message);

public sealed class PlaceRevisionConflictException : Exception;

public sealed class PlaceNotFoundException : Exception;

public sealed class PlaceValidationException(IReadOnlyDictionary<string, string[]> errors) : Exception
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
