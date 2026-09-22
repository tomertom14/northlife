namespace NorthLife.Api.Models;

public sealed class EventImage
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UploaderId { get; set; }
    public required string StorageKey { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }

    public AppUser Uploader { get; set; } = null!;
    public ICollection<Event> Events { get; set; } = [];
}
