using NorthLife.Api.Models;

namespace NorthLife.Api.Services;

public sealed class EventLifecycleService(TimeProvider timeProvider)
{
    public void SubmitNewByOwner(Event eventItem)
    {
        var now = timeProvider.GetUtcNow();
        EnsureNotExpired(eventItem, now);

        if (eventItem.Id == Guid.Empty)
        {
            eventItem.Id = Guid.CreateVersion7();
        }

        eventItem.Status = EventStatus.Pending;
        eventItem.IsHighlighted = false;
        eventItem.RejectionReason = null;
        eventItem.DeletedAtUtc = null;
        eventItem.CreatedAtUtc = now;
        eventItem.UpdatedAtUtc = now;
        eventItem.Revision = 1;
    }

    public void PublishNewByAdmin(Event eventItem)
    {
        SubmitNewByOwner(eventItem);
        eventItem.Status = EventStatus.Published;
    }

    public void ResubmitOwnerEdit(Event eventItem, int expectedRevision)
    {
        EnsureRevision(eventItem, expectedRevision);
        EnsureAvailable(eventItem);

        var now = timeProvider.GetUtcNow();
        EnsureNotExpired(eventItem, now);

        eventItem.Status = EventStatus.Pending;
        eventItem.IsHighlighted = false;
        eventItem.RejectionReason = null;
        Touch(eventItem, now);
    }

    public void Approve(Event eventItem, int expectedRevision)
    {
        EnsureRevision(eventItem, expectedRevision);
        EnsureAvailable(eventItem);
        EnsureStatus(eventItem, EventStatus.Pending);

        var now = timeProvider.GetUtcNow();
        EnsureNotExpired(eventItem, now);

        eventItem.Status = EventStatus.Published;
        eventItem.RejectionReason = null;
        Touch(eventItem, now);
    }

    public void Reject(Event eventItem, int expectedRevision, string reason)
    {
        EnsureRevision(eventItem, expectedRevision);
        EnsureAvailable(eventItem);
        EnsureStatus(eventItem, EventStatus.Pending);

        var normalizedReason = reason.Trim();
        if (string.IsNullOrWhiteSpace(normalizedReason))
        {
            throw new EventLifecycleException("A rejection reason is required.");
        }

        if (normalizedReason.Length > 1000)
        {
            throw new EventLifecycleException("A rejection reason cannot exceed 1000 characters.");
        }

        var now = timeProvider.GetUtcNow();
        eventItem.Status = EventStatus.Rejected;
        eventItem.IsHighlighted = false;
        eventItem.RejectionReason = normalizedReason;
        Touch(eventItem, now);
    }

    public void SetHighlight(Event eventItem, int expectedRevision, bool isHighlighted)
    {
        EnsureRevision(eventItem, expectedRevision);
        EnsureAvailable(eventItem);

        var now = timeProvider.GetUtcNow();
        EnsureNotExpired(eventItem, now);

        if (eventItem.Status != EventStatus.Published)
        {
            throw new EventLifecycleException("Only published events can be highlighted.");
        }

        eventItem.IsHighlighted = isHighlighted;
        Touch(eventItem, now);
    }

    public void EditByAdmin(Event eventItem, int expectedRevision)
    {
        EnsureRevision(eventItem, expectedRevision);
        EnsureAvailable(eventItem);

        var now = timeProvider.GetUtcNow();
        EnsureNotExpired(eventItem, now);
        if (eventItem.Status != EventStatus.Published)
        {
            eventItem.IsHighlighted = false;
        }
        Touch(eventItem, now);
    }

    public void Delete(Event eventItem, int expectedRevision)
    {
        EnsureRevision(eventItem, expectedRevision);
        EnsureAvailable(eventItem);

        var now = timeProvider.GetUtcNow();
        eventItem.IsHighlighted = false;
        eventItem.DeletedAtUtc = now;
        Touch(eventItem, now);
    }

    public bool IsExpired(Event eventItem) => eventItem.EndAtUtc <= timeProvider.GetUtcNow();

    private static void EnsureRevision(Event eventItem, int expectedRevision)
    {
        if (eventItem.Revision != expectedRevision)
        {
            throw new EventRevisionConflictException(expectedRevision, eventItem.Revision);
        }
    }

    private static void EnsureAvailable(Event eventItem)
    {
        if (eventItem.DeletedAtUtc is not null)
        {
            throw new EventLifecycleException("A deleted event cannot change state.");
        }
    }

    private static void EnsureStatus(Event eventItem, EventStatus expectedStatus)
    {
        if (eventItem.Status != expectedStatus)
        {
            throw new EventLifecycleException(
                $"Event must be {expectedStatus} for this operation.");
        }
    }

    private static void EnsureNotExpired(Event eventItem, DateTimeOffset now)
    {
        if (eventItem.EndAtUtc <= now)
        {
            throw new EventLifecycleException("Event end time must be in the future.");
        }

        if (eventItem.EndAtUtc <= eventItem.StartAtUtc)
        {
            throw new EventLifecycleException("Event end time must follow its start time.");
        }
    }

    private static void Touch(Event eventItem, DateTimeOffset now)
    {
        eventItem.UpdatedAtUtc = now;
        eventItem.Revision++;
    }
}
