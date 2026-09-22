namespace NorthLife.Api.Models;

public sealed class Event
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OwnerId { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public EventCategory Category { get; set; }
    public required string VenueName { get; set; }
    public required string Locality { get; set; }
    public required string Address { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public DateTimeOffset StartAtUtc { get; set; }
    public DateTimeOffset EndAtUtc { get; set; }
    public decimal Price { get; set; }
    public Guid ImageId { get; set; }
    public required string OrganizerName { get; set; }
    public string[] Tags { get; set; } = [];
    public EventStatus Status { get; set; } = EventStatus.Pending;
    public bool IsHighlighted { get; set; }
    public string? RejectionReason { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }
    public int Revision { get; set; } = 1;

    public AppUser Owner { get; set; } = null!;
    public EventImage Image { get; set; } = null!;
}
