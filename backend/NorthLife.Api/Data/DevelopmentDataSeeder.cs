using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NorthLife.Api.Models;
using System.Security.Cryptography;

namespace NorthLife.Api.Data;

public sealed class DevelopmentDataSeeder(
    AppDbContext dbContext,
    TimeProvider timeProvider)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        const string fixtureDomain = "@northlife.local";
        if (await dbContext.Users.AnyAsync(
                user => user.Email.EndsWith(fixtureDomain),
                cancellationToken))
        {
            return;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();

        var ownerOne = CreateUser(
            Guid.Parse("019924c0-0000-7000-8000-000000000001"),
            "נועה כהן",
            "owner.one@northlife.local",
            "גליל אירועים",
            UserRole.BusinessOwner,
            now);
        var ownerTwo = CreateUser(
            Guid.Parse("019924c0-0000-7000-8000-000000000002"),
            "דניאל לוי",
            "owner.two@northlife.local",
            "עמק חוויות",
            UserRole.BusinessOwner,
            now);
        var admin = CreateUser(
            Guid.Parse("019924c0-0000-7000-8000-000000000003"),
            "מנהל NorthLife",
            "admin@northlife.local",
            "NorthLife",
            UserRole.Admin,
            now);

        var imageOne = CreateImage(
            Guid.Parse("019924c0-0000-7000-9000-000000000001"),
            ownerOne.Id,
            "seed/music.webp",
            now);
        var imageTwo = CreateImage(
            Guid.Parse("019924c0-0000-7000-9000-000000000002"),
            ownerTwo.Id,
            "seed/outdoors.webp",
            now);
        var imageThree = CreateImage(
            Guid.Parse("019924c0-0000-7000-9000-000000000003"),
            admin.Id,
            "seed/workshop.webp",
            now);

        var events = new[]
        {
            CreateEvent(
                Guid.Parse("019924c0-0000-7000-a000-000000000001"),
                ownerOne.Id,
                imageOne.Id,
                "הופעה חיה בגליל",
                EventCategory.Music,
                EventStatus.Published,
                now.AddHours(-1),
                now.AddHours(2),
                60,
                true,
                now),
            CreateEvent(
                Guid.Parse("019924c0-0000-7000-a000-000000000002"),
                ownerTwo.Id,
                imageTwo.Id,
                "טיול זריחה בעמק",
                EventCategory.Outdoors,
                EventStatus.Published,
                now.AddDays(1).AddHours(2),
                now.AddDays(1).AddHours(6),
                0,
                false,
                now),
            CreateEvent(
                Guid.Parse("019924c0-0000-7000-a000-000000000003"),
                ownerOne.Id,
                imageOne.Id,
                "לילה של מוזיקה",
                EventCategory.Nightlife,
                EventStatus.Published,
                new DateTimeOffset(now.Year, now.Month, now.Day, 20, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(now.Year, now.Month, now.Day, 2, 0, 0, TimeSpan.Zero).AddDays(1),
                85,
                false,
                now),
            CreateEvent(
                Guid.Parse("019924c0-0000-7000-a000-000000000004"),
                ownerOne.Id,
                imageThree.Id,
                "סדנת קרמיקה",
                EventCategory.Workshops,
                EventStatus.Pending,
                now.AddDays(3),
                now.AddDays(3).AddHours(3),
                120,
                false,
                now),
            CreateEvent(
                Guid.Parse("019924c0-0000-7000-a000-000000000005"),
                ownerTwo.Id,
                imageTwo.Id,
                "אירוע שנדחה",
                EventCategory.Culture,
                EventStatus.Rejected,
                now.AddDays(4),
                now.AddDays(4).AddHours(2),
                30,
                false,
                now,
                "נדרש להשלים פרטי מיקום."),
            CreateEvent(
                Guid.Parse("019924c0-0000-7000-a000-000000000006"),
                ownerTwo.Id,
                imageTwo.Id,
                "אירוע שהסתיים",
                EventCategory.Sports,
                EventStatus.Published,
                now.AddDays(-2),
                now.AddDays(-2).AddHours(3),
                20,
                false,
                now),
        };

        dbContext.Users.AddRange(ownerOne, ownerTwo, admin);
        dbContext.EventImages.AddRange(imageOne, imageTwo, imageThree);
        dbContext.Events.AddRange(events);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static AppUser CreateUser(
        Guid id,
        string fullName,
        string email,
        string businessName,
        UserRole role,
        DateTimeOffset now)
    {
        var user = new AppUser
        {
            Id = id,
            FullName = fullName,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            PasswordHash = string.Empty,
            Phone = "0500000000",
            BusinessName = businessName,
            Role = role,
            CreatedAtUtc = now,
        };

        var undisclosedPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, undisclosedPassword);
        return user;
    }

    private static EventImage CreateImage(
        Guid id,
        Guid uploaderId,
        string storageKey,
        DateTimeOffset now) =>
        new()
        {
            Id = id,
            UploaderId = uploaderId,
            StorageKey = storageKey,
            ContentType = "image/webp",
            SizeBytes = 1,
            CreatedAtUtc = now,
        };

    private static Event CreateEvent(
        Guid id,
        Guid ownerId,
        Guid imageId,
        string title,
        EventCategory category,
        EventStatus status,
        DateTimeOffset start,
        DateTimeOffset end,
        decimal price,
        bool highlighted,
        DateTimeOffset now,
        string? rejectionReason = null) =>
        new()
        {
            Id = id,
            OwnerId = ownerId,
            ImageId = imageId,
            Title = title,
            Description = "אירוע לדוגמה עבור סביבת הפיתוח של NorthLife.",
            Category = category,
            VenueName = "מרכז קהילתי",
            Locality = "קריית שמונה",
            Address = "רחוב הגליל 1",
            Latitude = 33.2073m,
            Longitude = 35.5700m,
            StartAtUtc = start,
            EndAtUtc = end,
            Price = price,
            OrganizerName = "NorthLife Development",
            Tags = [category.ToString(), price == 0 ? "Free" : "Paid"],
            Status = status,
            IsHighlighted = highlighted,
            RejectionReason = rejectionReason,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Revision = 1,
        };
}
