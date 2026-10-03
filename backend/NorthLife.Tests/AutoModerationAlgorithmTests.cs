using NorthLife.Api.Contracts;
using NorthLife.Api.Models;
using NorthLife.Api.Moderation;
using NorthLife.Api.Recommendations;

namespace NorthLife.Tests;

public sealed class NorthRegionTests
{
    // The extreme coordinates of every locality in the demo catalogue, plus Haifa.
    [Theory]
    [InlineData("Beit She'an", 32.4920, 35.4960)]
    [InlineData("Afula", 32.6050, 35.2870)]
    [InlineData("Nazareth", 32.7020, 35.3060)]
    [InlineData("Tiberias", 32.7960, 35.5360)]
    [InlineData("Karmiel", 32.9090, 35.2920)]
    [InlineData("Akko", 32.9260, 35.0770)]
    [InlineData("Safed", 32.9700, 35.5010)]
    [InlineData("Rosh Pinna", 32.9690, 35.5460)]
    [InlineData("Katzrin", 32.9950, 35.6940)]
    [InlineData("Ma'alot-Tarshiha", 33.0200, 35.2650)]
    [InlineData("Kiryat Shmona", 33.2100, 35.5740)]
    [InlineData("Beit Hillel", 33.2060, 35.6080)]
    [InlineData("Tel Hai", 33.2380, 35.5770)]
    [InlineData("Metula north", 33.2810, 35.5830)]
    [InlineData("Metula west", 33.2720, 35.5700)]
    [InlineData("Majdal Shams", 33.2700, 35.7700)]
    [InlineData("Haifa", 32.7940, 34.9896)]
    [InlineData("Nahariya", 33.0060, 35.0940)]
    public void Northern_localities_are_inside(string name, double latitude, double longitude) =>
        Assert.True(NorthRegion.Contains(latitude, longitude), name);

    [Theory]
    [InlineData("Tel Aviv", 32.0853, 34.7818)]
    [InlineData("Netanya", 32.3215, 34.8532)]
    [InlineData("Hadera", 32.4340, 34.9196)]
    [InlineData("Jerusalem", 31.7683, 35.2137)]
    [InlineData("Eilat", 29.5577, 34.9519)]
    [InlineData("Jenin", 32.4607, 35.2961)]
    [InlineData("Irbid", 32.5556, 35.8500)]
    [InlineData("Bint Jbeil", 33.1200, 35.4330)]
    [InlineData("Beirut", 33.8938, 35.5018)]
    [InlineData("Amman", 31.9539, 35.9106)]
    public void Places_outside_the_north_are_outside(string name, double latitude, double longitude) =>
        Assert.False(NorthRegion.Contains(latitude, longitude), name);

    [Fact]
    public void Ray_casting_handles_a_concave_polygon()
    {
        // A "U": the notch between the arms is outside although it lies within the bounding box.
        (double, double)[] u = [(0, 0), (3, 0), (3, 3), (2, 3), (2, 1), (1, 1), (1, 3), (0, 3)];
        Assert.True(PointInPolygon.Contains(u, 0.5, 2));
        Assert.True(PointInPolygon.Contains(u, 2.5, 2));
        Assert.True(PointInPolygon.Contains(u, 1.5, 0.5));
        Assert.False(PointInPolygon.Contains(u, 1.5, 2));
        Assert.False(PointInPolygon.Contains(u, 4, 1));
    }
}

public sealed class ContentScannerTests
{
    private static readonly string[] Banned = ["הימורים", "קזינו", "casino", "הלוואה מהירה"];

    [Theory]
    [InlineData("ערב הימורים גדול", "הימורים")]
    [InlineData("ערב של שירה והימורים", "הימורים")]
    [InlineData("כל ההימורים סגורים", "הימורים")]
    [InlineData("ערב הִימוּרִים", "הימורים")]
    [InlineData("מסיבת קזינו", "קזינו")]
    [InlineData("Casino night", "casino")]
    [InlineData("קבלו הלוואה מהירה עכשיו", "הלוואה מהירה")]
    [InlineData("קבלו בהלוואה מהירה", "הלוואה מהירה")]
    public void Finds_banned_words_in_any_written_form(string text, string expected) =>
        Assert.Equal(expected, ContentScanner.FindBannedWord([text], Banned));

    [Theory]
    [InlineData("ערב מוזיקה בגליל")]
    [InlineData("הימור אחד קטן")]
    [InlineData("הלוואה ללא ריבית מהירה")]
    [InlineData("")]
    public void Leaves_ordinary_text_alone(string text) =>
        Assert.Null(ContentScanner.FindBannedWord([text], Banned));

    [Fact]
    public void An_empty_banned_list_matches_nothing() =>
        Assert.Null(ContentScanner.FindBannedWord(["ערב הימורים"], []));

    [Theory]
    [InlineData("להזמנות: 054-1234567", "phone")]
    [InlineData("התקשרו 0541234567 עכשיו", "phone")]
    [InlineData("וואטסאפ +972 54 123 4567", "phone")]
    [InlineData("טלפון 04-6912345", "phone")]
    [InlineData("טלפון (04) 6912345", "phone")]
    [InlineData("מוקד 1-700-700-700", "phone")]
    [InlineData("054-1234567 052-7654321", "phone")]
    [InlineData("כרטיסים ב-www.tickets.co.il", "link")]
    [InlineData("הרשמה: https://example.com/event", "link")]
    [InlineData("פרטים ב galillive.co.il", "link")]
    [InlineData("קישור bit.ly/abc", "link")]
    [InlineData("כתבו ל info@galil.co.il", "email")]
    public void Finds_contact_details(string text, string kind) =>
        Assert.Contains(kind, ContentScanner.FindContactDetails([text]));

    [Theory]
    [InlineData("כניסה 120 ₪")]
    [InlineData("פסטיבל 2026")]
    [InlineData("פתיחת דלתות 20:00, הופעה 21:30")]
    [InlineData("1-3 באוקטובר 2026")]
    [InlineData("בתאריך 03.10.2026")]
    [InlineData("גילאי 18+")]
    [InlineData("ק.ש. וגליל עליון")]
    [InlineData("מחיר 50.00 לאדם")]
    [InlineData("מספר 20541234567 בקטלוג")]
    public void Ignores_prices_years_dates_and_times(string text) =>
        Assert.Empty(ContentScanner.FindContactDetails([text]));

    [Fact]
    public void An_email_address_is_not_also_reported_as_a_link() =>
        Assert.Equal(["email"], ContentScanner.FindContactDetails(["info@galil.co.il"]));
}

public sealed class DuplicateFinderTests
{
    private static readonly DateOnly Day = new(2026, 10, 15);

    private static ItemText Text(Guid id, string title, string description) =>
        new(id, title, description, ["ג׳אז"], EventCategory.Music, "צפת");

    [Fact]
    public void Flags_a_copy_on_the_same_day_nearby_but_not_other_days_or_far_away()
    {
        Guid subject = Guid.NewGuid(), copy = Guid.NewGuid(), otherDay = Guid.NewGuid(), farAway = Guid.NewGuid(), different = Guid.NewGuid();
        const string description = "הרכב הג׳אז של הגליל מארח את הסקסופוניסט אבי לוי לערב של סטנדרטים ואלתורים על הגג";
        var model = TfIdfModel.Build([
            Text(subject, "ערב ג׳אז על הגג", description),
            Text(copy, "ערב ג׳אז על הגג", description),
            Text(otherDay, "ערב ג׳אז על הגג", description),
            Text(farAway, "ערב ג׳אז על הגג", description),
            Text(different, "סדנת קרמיקה לילדים", "סדנה משפחתית ליצירה בחומר, כולל שריפה בתנור והכנת ספלים"),
        ]);
        var candidate = new DuplicateCandidate(subject, "ערב ג׳אז על הגג", 32.9646, 35.4960, Day);
        DuplicateCandidate[] others =
        [
            new(copy, "ערב ג׳אז על הגג", 32.9660, 35.4975, Day), // about 200 m away
            new(otherDay, "ערב ג׳אז על הגג", 32.9646, 35.4960, Day.AddDays(1)),
            new(farAway, "ערב ג׳אז על הגג", 32.9646, 35.5500, Day), // about 5 km away
            new(different, "סדנת קרמיקה לילדים", 32.9646, 35.4960, Day),
        ];

        var match = DuplicateFinder.Find(candidate, others, model.Cosine, 0.85, 1000);

        Assert.NotNull(match);
        Assert.Equal(copy, match.EventId);
        Assert.InRange(match.Similarity, 0.99, 1.0001);
    }

    [Fact]
    public void Light_rewording_stays_above_the_threshold_and_a_different_event_below()
    {
        Guid subject = Guid.NewGuid(), reworded = Guid.NewGuid(), neighbour = Guid.NewGuid(), filler = Guid.NewGuid();
        const string description = "הרכב הג׳אז של הגליל מארח את הסקסופוניסט אבי לוי לערב של סטנדרטים ואלתורים על הגג";
        var model = TfIdfModel.Build([
            Text(subject, "ערב ג׳אז על הגג", description),
            Text(reworded, "ערב ג׳אז על הגג!", description + " בואו מוקדם"),
            Text(neighbour, "ערב פולק אקוסטי", "זמרת היוצרים נועה כהן בשירים מקוריים ובגרסאות כיסוי אקוסטיות"),
            Text(filler, "שוק איכרים", "תוצרת מקומית, גבינות, לחמים ודבש מהגליל"),
        ]);
        var candidate = new DuplicateCandidate(subject, "ערב ג׳אז על הגג", 32.9646, 35.4960, Day);

        var rewordedMatch = DuplicateFinder.Find(candidate, [new(reworded, "ערב ג׳אז על הגג!", 32.9646, 35.4960, Day)], model.Cosine, 0.85, 1000);
        var differentMatch = DuplicateFinder.Find(candidate, [new(neighbour, "ערב פולק אקוסטי", 32.9646, 35.4960, Day)], model.Cosine, 0.85, 1000);

        Assert.NotNull(rewordedMatch);
        Assert.Null(differentMatch);
    }
}

public sealed class ModerationRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 4, 0, 0, TimeSpan.Zero);
    private static readonly AutoModerationSettings Settings = new();

    private static ModerationCandidate Candidate(
        double latitude = 32.9646,
        double longitude = 35.4960,
        TimeSpan? startsIn = null,
        TimeSpan? duration = null,
        decimal price = 80,
        string description = "ערב של מוזיקה חיה במרפסת הגג עם הרכב מקומי") =>
        new(
            Guid.NewGuid(), 1, Guid.NewGuid(), "ערב מוזיקה על הגג", description, ["מוזיקה"], "גג העיר", "גליל לייב",
            latitude, longitude, Now + (startsIn ?? TimeSpan.FromDays(3)), Now + (startsIn ?? TimeSpan.FromDays(3)) + (duration ?? TimeSpan.FromHours(3)),
            price);

    private static OwnerRecord Owner(
        double accountAgeDays = 30,
        int published = 3,
        double? rejectedDaysAgo = null,
        int autoApprovalsToday = 0,
        bool emailConfirmed = true,
        bool suspended = false) =>
        new(Now - TimeSpan.FromDays(accountAgeDays), emailConfirmed, suspended, published,
            rejectedDaysAgo is { } days ? Now - TimeSpan.FromDays(days) : null, autoApprovalsToday);

    private static IReadOnlyList<string> Codes(ModerationCandidate candidate, OwnerRecord owner, DuplicateMatch? duplicate = null, int passedEarlier = 0) =>
        ModerationRules.Evaluate(candidate, owner, duplicate, Settings, Now, passedEarlier).Select(reason => reason.Code).ToList();

    [Fact]
    public void A_trusted_owner_with_a_clean_event_passes_every_term() =>
        Assert.Empty(Codes(Candidate(), Owner()));

    [Theory]
    [InlineData(6.9, true)]
    [InlineData(7, false)]
    public void Account_age_boundary(double days, bool held) =>
        Assert.Equal(held, Codes(Candidate(), Owner(accountAgeDays: days)).Contains("owner_new_account"));

    [Theory]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void Approved_events_boundary(int published, bool held) =>
        Assert.Equal(held, Codes(Candidate(), Owner(published: published)).Contains("owner_few_approvals"));

    [Theory]
    [InlineData(89, true)]
    [InlineData(91, false)]
    public void Recent_rejection_boundary(double daysAgo, bool held) =>
        Assert.Equal(held, Codes(Candidate(), Owner(rejectedDaysAgo: daysAgo)).Contains("owner_recent_rejection"));

    [Theory]
    [InlineData(4, 0, false)]
    [InlineData(5, 0, true)]
    [InlineData(3, 2, true)]
    public void Daily_cap_counts_earlier_approvals_today_and_in_this_run(int today, int thisRun, bool held) =>
        Assert.Equal(held, Codes(Candidate(), Owner(autoApprovalsToday: today), passedEarlier: thisRun).Contains("owner_daily_cap"));

    [Fact]
    public void Suspended_or_unverified_owners_are_held()
    {
        Assert.Contains("owner_suspended", Codes(Candidate(), Owner(suspended: true)));
        Assert.Contains("owner_email_unconfirmed", Codes(Candidate(), Owner(emailConfirmed: false)));
    }

    [Fact]
    public void Events_outside_the_north_are_held() =>
        Assert.Equal(["outside_region"], Codes(Candidate(latitude: 32.0853, longitude: 34.7818), Owner()));

    [Theory]
    [InlineData(180, false)]
    [InlineData(181, true)]
    public void Start_date_horizon_boundary(int days, bool held) =>
        Assert.Equal(held, Codes(Candidate(startsIn: TimeSpan.FromDays(days)), Owner()).Contains("too_far_ahead"));

    [Fact]
    public void Duration_boundary()
    {
        Assert.DoesNotContain("too_long", Codes(Candidate(duration: TimeSpan.FromDays(14)), Owner()));
        Assert.Contains("too_long", Codes(Candidate(duration: TimeSpan.FromDays(14) + TimeSpan.FromMinutes(1)), Owner()));
    }

    [Fact]
    public void Price_boundary()
    {
        Assert.DoesNotContain("price_too_high", Codes(Candidate(price: 1000m), Owner()));
        Assert.Contains("price_too_high", Codes(Candidate(price: 1000.01m), Owner()));
    }

    [Fact]
    public void Ended_events_are_held() =>
        Assert.Contains("ended", Codes(Candidate(startsIn: TimeSpan.FromHours(-5), duration: TimeSpan.FromHours(2)), Owner()));

    [Fact]
    public void Text_findings_and_duplicates_become_reasons()
    {
        var reasons = ModerationRules.Evaluate(
            Candidate(description: "ערב קזינו, הזמנות ב-054-1234567"),
            Owner(),
            new DuplicateMatch(Guid.NewGuid(), "ערב מוזיקה על הגג", 0.97),
            Settings,
            Now,
            0);

        Assert.Equal(["banned_word", "contact_details", "possible_duplicate"], reasons.Select(reason => reason.Code));
        Assert.Equal("קזינו", reasons[0].Values!["word"]);
        Assert.Equal("phone", reasons[1].Values!["kind"]);
        Assert.Equal(0.97, reasons[2].Values!["similarity"]);
    }

    [Fact]
    public void Every_failed_term_is_reported_not_only_the_first()
    {
        var codes = Codes(Candidate(latitude: 31.7683, longitude: 35.2137, price: 5000), Owner(accountAgeDays: 1, published: 0));
        Assert.Equal(["owner_new_account", "owner_few_approvals", "outside_region", "price_too_high"], codes);
    }
}

public sealed class AutoModerationScheduleTests
{
    private static readonly TimeOnly Seven = new(7, 0);

    [Fact]
    public void Due_only_after_the_run_time_and_once_a_day()
    {
        var beforeRun = new DateTimeOffset(2026, 10, 1, 3, 59, 0, TimeSpan.Zero); // 06:59 Israel summer time
        var afterRun = new DateTimeOffset(2026, 10, 1, 4, 0, 0, TimeSpan.Zero); // 07:00
        var today = new DateOnly(2026, 10, 1);

        Assert.False(AutoModerationSchedule.IsDue(beforeRun, Seven, null));
        Assert.True(AutoModerationSchedule.IsDue(afterRun, Seven, null));
        Assert.True(AutoModerationSchedule.IsDue(afterRun, Seven, today.AddDays(-1)));
        Assert.False(AutoModerationSchedule.IsDue(afterRun, Seven, today));
    }

    [Fact]
    public void A_missed_run_is_caught_up_later_the_same_day()
    {
        var afternoon = new DateTimeOffset(2026, 10, 1, 11, 0, 0, TimeSpan.Zero); // 14:00 Israel time
        Assert.True(AutoModerationSchedule.IsDue(afternoon, Seven, new DateOnly(2026, 9, 30)));
    }

    [Fact]
    public void The_Israel_date_changes_at_local_midnight()
    {
        // 23:30 UTC on 30 September is already 1 October in Israel (UTC+3).
        Assert.Equal(new DateOnly(2026, 10, 1), AutoModerationSchedule.LocalDate(new DateTimeOffset(2026, 9, 30, 23, 30, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Run_time_follows_daylight_saving_time()
    {
        // Israel moves to summer time on Friday 27 March 2026 and back on Sunday 25 October 2026.
        Assert.Equal(new DateTimeOffset(2026, 3, 26, 5, 0, 0, TimeSpan.Zero), AutoModerationSchedule.RunInstant(new DateOnly(2026, 3, 26), Seven));
        Assert.Equal(new DateTimeOffset(2026, 3, 27, 4, 0, 0, TimeSpan.Zero), AutoModerationSchedule.RunInstant(new DateOnly(2026, 3, 27), Seven));
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 4, 0, 0, TimeSpan.Zero), AutoModerationSchedule.RunInstant(new DateOnly(2026, 10, 24), Seven));
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 5, 0, 0, TimeSpan.Zero), AutoModerationSchedule.RunInstant(new DateOnly(2026, 10, 25), Seven));
    }

    [Fact]
    public void A_run_time_inside_the_spring_forward_gap_moves_one_hour_later()
    {
        // 02:30 does not exist on 27 March 2026; the run happens at 03:30 summer time (00:30 UTC).
        Assert.Equal(
            new DateTimeOffset(2026, 3, 27, 0, 30, 0, TimeSpan.Zero),
            AutoModerationSchedule.RunInstant(new DateOnly(2026, 3, 27), new TimeOnly(2, 30)));
    }

    [Fact]
    public void Next_run_is_today_until_claimed_then_tomorrow()
    {
        var morning = new DateTimeOffset(2026, 10, 1, 2, 0, 0, TimeSpan.Zero); // 05:00 Israel time
        var today = new DateOnly(2026, 10, 1);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 4, 0, 0, TimeSpan.Zero), AutoModerationSchedule.NextRun(morning, Seven, null));
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 4, 0, 0, TimeSpan.Zero), AutoModerationSchedule.NextRun(morning, Seven, today));
    }
}

public sealed class AutoModerationSettingsValidatorTests
{
    private static AutoModerationSettingsRequest Valid() => new(
        AutoModerationMode.Approve, "07:00", 7, 3, 90, 5, 180, 14, 1000, 0.85, 1000, ["קזינו", " הימורים ", "קזינו"]);

    [Fact]
    public void Defaults_are_valid_and_banned_words_are_trimmed_and_deduplicated()
    {
        var normalized = AutoModerationSettingsValidator.Validate(Valid());
        Assert.Equal(new TimeOnly(7, 0), normalized.RunAtLocal);
        Assert.Equal(["קזינו", "הימורים"], normalized.BannedWords);
    }

    [Fact]
    public void Out_of_range_values_are_rejected_with_field_errors()
    {
        var request = Valid() with
        {
            RunAt = "25:00",
            MinApprovedEvents = -1,
            MaxAutoApprovalsPerOwnerPerDay = 0,
            DuplicateSimilarity = 0.4,
            MaxPrice = -1,
            BannedWords = [new string('א', 51)],
        };

        var exception = Assert.Throws<AutoModerationSettingsException>(() => AutoModerationSettingsValidator.Validate(request));

        Assert.Equal(
            ["bannedWords", "duplicateSimilarity", "maxAutoApprovalsPerOwnerPerDay", "maxPrice", "minApprovedEvents", "runAt"],
            exception.Errors.Keys.Order());
    }

    [Fact]
    public void Too_many_banned_words_are_rejected()
    {
        var request = Valid() with { BannedWords = Enumerable.Range(0, 201).Select(index => $"מילה{index}").ToArray() };
        Assert.Throws<AutoModerationSettingsException>(() => AutoModerationSettingsValidator.Validate(request));
    }
}
