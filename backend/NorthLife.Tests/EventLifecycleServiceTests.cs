using NorthLife.Api.Models;
using NorthLife.Api.Services;

namespace NorthLife.Tests;

public sealed class EventLifecycleServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private readonly EventLifecycleService _service =
        new(new FixedTimeProvider(Now));

    [Fact]
    public void Owner_submission_is_pending_and_not_highlighted()
    {
        var eventItem = CreateEvent(EventStatus.Published);
        eventItem.IsHighlighted = true;
        eventItem.RejectionReason = "old reason";

        _service.SubmitNewByOwner(eventItem);

        Assert.Equal(EventStatus.Pending, eventItem.Status);
        Assert.False(eventItem.IsHighlighted);
        Assert.Null(eventItem.RejectionReason);
        Assert.Equal(1, eventItem.Revision);
        Assert.Equal(Now, eventItem.CreatedAtUtc);
        Assert.Equal(Now, eventItem.UpdatedAtUtc);
    }

    [Fact]
    public void Editing_published_event_returns_it_to_pending()
    {
        var eventItem = CreateEvent(EventStatus.Published);
        eventItem.IsHighlighted = true;
        eventItem.Revision = 7;

        _service.ResubmitOwnerEdit(eventItem, 7);

        Assert.Equal(EventStatus.Pending, eventItem.Status);
        Assert.False(eventItem.IsHighlighted);
        Assert.Equal(8, eventItem.Revision);
        Assert.Equal(Now, eventItem.UpdatedAtUtc);
    }

    [Fact]
    public void Approval_publishes_pending_event()
    {
        var eventItem = CreateEvent(EventStatus.Pending);
        eventItem.Revision = 3;

        _service.Approve(eventItem, 3);

        Assert.Equal(EventStatus.Published, eventItem.Status);
        Assert.Equal(4, eventItem.Revision);
    }

    [Fact]
    public void Stale_revision_is_rejected_without_mutation()
    {
        var eventItem = CreateEvent(EventStatus.Pending);
        eventItem.Revision = 4;

        var exception = Assert.Throws<EventRevisionConflictException>(
            () => _service.Approve(eventItem, 3));

        Assert.Contains("Expected 3, actual 4", exception.Message);
        Assert.Equal(EventStatus.Pending, eventItem.Status);
        Assert.Equal(4, eventItem.Revision);
    }

    [Fact]
    public void Expired_event_cannot_be_approved()
    {
        var eventItem = CreateEvent(EventStatus.Pending);
        eventItem.StartAtUtc = Now.AddHours(-2);
        eventItem.EndAtUtc = Now;

        Assert.Throws<EventLifecycleException>(
            () => _service.Approve(eventItem, eventItem.Revision));

        Assert.Equal(EventStatus.Pending, eventItem.Status);
    }

    [Fact]
    public void Rejection_requires_a_reason()
    {
        var eventItem = CreateEvent(EventStatus.Pending);

        Assert.Throws<EventLifecycleException>(
            () => _service.Reject(eventItem, eventItem.Revision, "   "));

        Assert.Equal(EventStatus.Pending, eventItem.Status);
    }

    [Fact]
    public void Admin_edit_preserves_published_status_and_advances_revision()
    {
        var eventItem = CreateEvent(EventStatus.Published);
        eventItem.Revision = 5;

        _service.EditByAdmin(eventItem, 5);

        Assert.Equal(EventStatus.Published, eventItem.Status);
        Assert.Equal(6, eventItem.Revision);
    }

    private static Event CreateEvent(EventStatus status) =>
        new()
        {
            OwnerId = Guid.CreateVersion7(),
            ImageId = Guid.CreateVersion7(),
            Title = "Test event",
            Description = "Test description",
            Category = EventCategory.Culture,
            VenueName = "Test venue",
            Locality = "Kiryat Shmona",
            Address = "Test address",
            Latitude = 33.2m,
            Longitude = 35.5m,
            StartAtUtc = Now.AddHours(1),
            EndAtUtc = Now.AddHours(3),
            Price = 0,
            OrganizerName = "Test organizer",
            Status = status,
            CreatedAtUtc = Now.AddDays(-1),
            UpdatedAtUtc = Now.AddDays(-1),
        };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
