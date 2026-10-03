using Microsoft.EntityFrameworkCore;
using NorthLife.Api.Data;
using NorthLife.Api.Models;

namespace NorthLife.Api.Places;

/// <summary>
/// Part of "--seed-demo": published places for the six demo businesses, with realistic weekly hours (split days,
/// Friday short hours, Saturday night after Shabbat, bars open past midnight and two places open around the clock),
/// and a link from some of each owner's upcoming events to the matching place. Runs when the demo owners exist and
/// have no places yet, so it also upgrades a database seeded before places existed.
/// </summary>
public sealed class DemoPlacesSeeder(AppDbContext dbContext, TimeProvider timeProvider)
{
    private sealed record Spec(
        int Owner,
        PlaceCategory Category,
        EventCategory Cover,
        string Name,
        string Locality,
        decimal LatitudeOffset,
        decimal LongitudeOffset,
        string Description,
        string? Perk,
        string? Phone,
        string? Instagram,
        (int Day, string Opens, string Closes)[] Hours,
        EventCategory[] Events);

    private static readonly int[] Weekdays = [0, 1, 2, 3, 4];
    private static readonly int[] EveryDay = [0, 1, 2, 3, 4, 5, 6];

    private static readonly Spec[] Catalog =
    [
        new(0, PlaceCategory.Nightlife, EventCategory.Nightlife, "גליל לייב – בר הופעות", "קריית שמונה", 0.002m, -0.003m,
            "בר הופעות קטן עם במה פתוחה בימי שני, להקות מקומיות בסופי שבוע ומטבח עד מאוחר.",
            "חצי ליטר ב-20 ₪ בהצגת תעודת סטודנט", "04-6901234", "galil.live",
            [.. Days([0, 1, 2, 3], "20:00", "01:00"), (4, "20:00", "03:00"), (5, "21:00", "03:00"), (6, "21:00", "02:00")],
            [EventCategory.Music, EventCategory.Nightlife]),
        new(0, PlaceCategory.Nightlife, EventCategory.Music, "בר הגג תל חי", "תל חי", 0.001m, 0.002m,
            "בר על הגג ליד הקמפוס, עם די־ג׳יי בחמישי ומסיבות סוף סמסטר.",
            "הנחה של 15% לסטודנטים בימים א׳–ד׳", null, "rooftop.telhai",
            [.. Days([0, 1, 2, 3, 4], "21:00", "02:00"), (5, "22:00", "04:00")],
            [EventCategory.Nightlife]),
        new(1, PlaceCategory.Food, EventCategory.Food, "טעמים מהצפון – מסעדה", "ראש פינה", -0.002m, 0.001m,
            "מסעדת שף גלילית: ירקות מהחקלאים באזור, גבינות מהמחלבות הקטנות ויין מהיקבים של הרמה.",
            null, "04-6934567", "teamim.hatzafon",
            [.. Days(Weekdays, "12:00", "23:00"), (5, "11:00", "16:00"), (6, "19:00", "23:30")],
            [EventCategory.Food]),
        new(1, PlaceCategory.Cafe, EventCategory.Food, "קפה הסטודנטים", "תל חי", -0.001m, -0.001m,
            "בית קפה בכניסה לקמפוס עם שקעים בכל שולחן, מרק ביום קר ופינת לימוד שקטה.",
            "קפה ומאפה ב-15 ₪ עם תעודת סטודנט", "04-6955555", null,
            [.. Days(Weekdays, "07:30", "20:00"), (5, "08:00", "13:00")],
            [EventCategory.Food]),
        new(1, PlaceCategory.Food, EventCategory.Food, "מאפיית הגליל 24/7", "קריית שמונה", -0.003m, 0.002m,
            "מאפייה שלא נסגרת: לחם חם, בורקס ושתייה חמה בכל שעה, גם אחרי מסיבה.",
            null, "04-6908888", null,
            [.. Days(EveryDay, "00:00", "00:00")],
            []),
        new(2, PlaceCategory.Classes, EventCategory.Workshops, "סטודיו חימר בהר", "צפת", 0.002m, 0.002m,
            "סטודיו לקרמיקה ועבודה באובניים בעיר העתיקה: חוגים שבועיים, סדנאות ערב ושעות סטודיו פתוחות.",
            "10% הנחה על חוג שבועי לסטודנטים", "050-7771234", "clay.safed",
            [.. Days(Weekdays, "10:00", "13:00"), .. Days(Weekdays, "16:00", "20:00"), (5, "09:00", "13:00")],
            [EventCategory.Workshops]),
        new(2, PlaceCategory.Classes, EventCategory.Workshops, "בית המלאכה כרמיאל", "כרמיאל", -0.002m, 0.001m,
            "סדנאות נגרות, תכשיטים ורקמה בקבוצות קטנות, לכל הרמות.",
            null, "04-9981234", null,
            [.. Days([0, 1, 2, 3], "16:00", "21:00"), (5, "09:00", "14:00")],
            [EventCategory.Workshops]),
        new(3, PlaceCategory.Outdoors, EventCategory.Outdoors, "מרכז הקיאקים בירדן", "קריית שמונה", 0.02m, 0.04m,
            "השכרת קיאקים ואבובים על הירדן, מסלולים של שעה עד שלוש שעות ומדריכים בסופי שבוע.",
            "כניסה מוזלת לקבוצות סטודנטים", "04-6902020", "jordan.kayaks",
            [.. Days(EveryDay, "08:00", "17:00")],
            [EventCategory.Outdoors]),
        new(3, PlaceCategory.Sports, EventCategory.Sports, "חדר הכושר תל חי", "תל חי", 0.002m, -0.002m,
            "חדר כושר עם אזור משקולות, סטודיו לשיעורים ובריכה מקורה בחורף.",
            "מנוי סמסטריאלי לסטודנטים ב-99 ₪ לחודש", "04-6953333", null,
            [.. Days(Weekdays, "06:00", "23:00"), (5, "07:00", "15:00"), (6, "09:00", "14:00")],
            [EventCategory.Sports]),
        new(4, PlaceCategory.Culture, EventCategory.Culture, "תיאטרון הצפון", "כרמיאל", 0.001m, 0.001m,
            "אולם של 400 מקומות עם הצגות, מופעי מחול וקולנוע בימי רביעי.",
            "כרטיס סטודנט ב-40 ₪ לכל ההצגות", "04-9887777", "theater.north",
            [.. Days(Weekdays, "10:00", "22:00"), (6, "19:00", "23:00")],
            [EventCategory.Culture]),
        new(4, PlaceCategory.Culture, EventCategory.Culture, "הגלריה העירונית נהריה", "נהריה", -0.001m, 0.002m,
            "גלריה לאמנות עכשווית של אמנים מהצפון, עם תערוכה מתחלפת בכל חודש.",
            null, null, null,
            [.. Days([0, 1, 2, 3], "10:00", "18:00"), (4, "10:00", "20:00"), (5, "10:00", "14:00")],
            [EventCategory.Culture]),
        new(5, PlaceCategory.Services, EventCategory.Other, "מכבסה אוטומטית 24/7", "קריית שמונה", 0.003m, 0.003m,
            "מכונות כביסה וייבוש בתשלום באפליקציה, פתוח כל השבוע מסביב לשעון.",
            null, null, null,
            [.. Days(EveryDay, "00:00", "00:00")],
            []),
        new(5, PlaceCategory.Services, EventCategory.Other, "מרכז הצעירים קריית שמונה", "קריית שמונה", -0.001m, 0.004m,
            "מרחב לימוד ועבודה עם אינטרנט מהיר, חדרי ישיבות וייעוץ תעסוקתי לצעירים.",
            "חדר לימוד חינם לסטודנטים", "04-6907070", null,
            [.. Days(Weekdays, "09:00", "22:00")],
            [EventCategory.Other]),
    ];

    public async Task<int> SeedAsync(CancellationToken cancellationToken)
    {
        var owners = await dbContext.Users
            .Where(user => user.Email.EndsWith(DemoCatalog.EmailDomain) && user.Email.StartsWith("owner"))
            .ToListAsync(cancellationToken);
        if (owners.Count < DemoCatalog.Owners.Length) return 0;
        var ownerIds = owners.Select(owner => owner.Id).ToList();
        if (await dbContext.Places.IgnoreQueryFilters().AnyAsync(place => ownerIds.Contains(place.OwnerId), cancellationToken)) return 0;

        var byIndex = owners.ToDictionary(owner => int.Parse(owner.Email[5..owner.Email.IndexOf('@')]) - 1);
        var covers = await dbContext.EventImages
            .Where(image => image.StorageKey.StartsWith("demo/"))
            .ToDictionaryAsync(image => image.StorageKey, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var upcoming = await dbContext.Events
            .Where(eventItem => ownerIds.Contains(eventItem.OwnerId) && eventItem.PlaceId == null &&
                eventItem.Status == EventStatus.Published && eventItem.EndAtUtc > now)
            .OrderBy(eventItem => eventItem.StartAtUtc)
            .ToListAsync(cancellationToken);

        foreach (var spec in Catalog)
        {
            var owner = byIndex[spec.Owner];
            var locality = DemoCatalog.Localities.Single(candidate => candidate.Name == spec.Locality);
            if (!covers.TryGetValue($"demo/{spec.Cover.ToString().ToLowerInvariant()}.webp", out var cover)) return 0;

            var place = new Place
            {
                OwnerId = owner.Id,
                Name = spec.Name,
                Category = spec.Category,
                Description = spec.Description,
                Locality = spec.Locality,
                Address = $"{spec.Name}, {spec.Locality}",
                Latitude = locality.Latitude + spec.LatitudeOffset,
                Longitude = locality.Longitude + spec.LongitudeOffset,
                Phone = spec.Phone,
                Instagram = spec.Instagram,
                StudentPerk = spec.Perk,
                ImageId = cover.Id,
                Status = EventStatus.Published,
                CreatedAtUtc = now.AddDays(-30),
                UpdatedAtUtc = now.AddDays(-30),
            };
            place.OpeningHours = spec.Hours
                .Select(hours => new PlaceOpeningHours
                {
                    PlaceId = place.Id,
                    DayOfWeek = (short)hours.Day,
                    OpensMinute = Minutes(hours.Opens),
                    ClosesMinute = Minutes(hours.Closes),
                })
                .ToList();
            dbContext.Places.Add(place);

            // Two of the owner's upcoming events move to the place, so its page lists something.
            foreach (var eventItem in upcoming.Where(item => item.OwnerId == owner.Id && item.PlaceId == null && spec.Events.Contains(item.Category)).Take(2).ToList())
            {
                eventItem.PlaceId = place.Id;
                eventItem.VenueName = place.Name;
                eventItem.Locality = place.Locality;
                eventItem.Address = place.Address;
                eventItem.Latitude = place.Latitude;
                eventItem.Longitude = place.Longitude;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Catalog.Length;
    }

    private static IEnumerable<(int Day, string Opens, string Closes)> Days(int[] days, string opens, string closes) =>
        days.Select(day => (day, opens, closes));

    private static short Minutes(string time) => (short)(int.Parse(time[..2]) * 60 + int.Parse(time[3..]));
}
