namespace NorthLife.Api.Models;

public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string FullName { get; set; }
    public required string Email { get; set; }
    public required string NormalizedEmail { get; set; }
    public required string PasswordHash { get; set; }
    public required string Phone { get; set; }
    public required string BusinessName { get; set; }
    public UserRole Role { get; set; } = UserRole.BusinessOwner;
    public DateTimeOffset CreatedAtUtc { get; set; }

    public ICollection<Event> Events { get; set; } = [];
    public ICollection<EventImage> UploadedImages { get; set; } = [];
}
