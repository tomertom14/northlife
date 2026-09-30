namespace NorthLife.Api.Models;

/// <summary>
/// A permanent venue or business (restaurant, café, bar, studio, local service) with a public profile page.
/// It follows the event lifecycle: owners submit, an administrator approves, and edits go back to review.
/// </summary>
public sealed class Place
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OwnerId { get; set; }
    public required string Name { get; set; }
    public PlaceCategory Category { get; set; }
    public required string Description { get; set; }
    public required string Locality { get; set; }
    public required string Address { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public string? Phone { get; set; }
    public string? Website { get; set; }
    public string? Instagram { get; set; }

    /// <summary>A student discount or perk, shown as a badge; the platform's audience is students and young people.</summary>
    public string? StudentPerk { get; set; }

    public Guid ImageId { get; set; }
    public EventStatus Status { get; set; } = EventStatus.Pending;
    public string? RejectionReason { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }
    public int Revision { get; set; } = 1;

    /// <summary>Geohash of the place (precision 9), kept in sync on save for "near me" searches.</summary>
    public string Geohash { get; set; } = string.Empty;

    public AppUser Owner { get; set; } = null!;
    public EventImage Image { get; set; } = null!;
    public List<PlaceOpeningHours> OpeningHours { get; set; } = [];
}

/// <summary>
/// One opening interval on one day of the week, in Israel local time. Minutes count from midnight.
/// A closing minute at or before the opening minute means the place closes on the next day, so
/// 20:00–02:00 is a late bar and 00:00–00:00 is open around the clock.
/// </summary>
public sealed class PlaceOpeningHours
{
    public Guid PlaceId { get; set; }

    /// <summary>0 = Sunday … 6 = Saturday, as in <see cref="DayOfWeek"/>.</summary>
    public short DayOfWeek { get; set; }

    public short OpensMinute { get; set; }
    public short ClosesMinute { get; set; }
}

public enum PlaceCategory
{
    Food,
    Cafe,
    Nightlife,
    Classes,
    Sports,
    Culture,
    Outdoors,
    Services,
}
