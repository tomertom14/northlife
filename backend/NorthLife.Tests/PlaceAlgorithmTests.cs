using NorthLife.Api.Contracts;
using NorthLife.Api.Models;
using NorthLife.Api.Places;

namespace NorthLife.Tests;

public sealed class PlaceAlgorithmTests
{
    private const int Sunday = 0, Monday = 1, Tuesday = 2, Thursday = 4, Friday = 5, Saturday = 6;

    private static int At(int hour, int minute = 0) => hour * 60 + minute;

    [Theory]
    [InlineData(8, 59, false)]
    [InlineData(9, 0, true)]
    [InlineData(16, 59, true)]
    [InlineData(17, 0, false)]
    public void Same_day_hours_open_at_the_opening_minute_and_close_at_the_closing_minute(int hour, int minute, bool open)
    {
        HoursInterval[] hours = [new(Monday, At(9), At(17))];

        Assert.Equal(open, OpeningHours.IsOpen(hours, new WeekTime(Monday, At(hour, minute))));
    }

    [Fact]
    public void A_late_bar_stays_open_past_midnight_into_the_next_day()
    {
        HoursInterval[] hours = [new(Thursday, At(20), At(2))];

        Assert.False(OpeningHours.IsOpen(hours, new WeekTime(Thursday, At(19, 59))));
        Assert.True(OpeningHours.IsOpen(hours, new WeekTime(Thursday, At(23, 30))));
        Assert.True(OpeningHours.IsOpen(hours, new WeekTime(Friday, At(1, 59))));
        Assert.False(OpeningHours.IsOpen(hours, new WeekTime(Friday, At(2))));
        Assert.False(OpeningHours.IsOpen(hours, new WeekTime(Friday, At(21))));
    }

    [Fact]
    public void Saturday_night_wraps_into_sunday_morning()
    {
        HoursInterval[] hours = [new(Saturday, At(22), At(3))];

        Assert.True(OpeningHours.IsOpen(hours, new WeekTime(Sunday, At(2, 30))));
        var status = OpeningHours.Status(hours, new WeekTime(Saturday, At(23)));

        Assert.True(status.IsOpen);
        Assert.Equal(new NextChange("closes", Sunday, At(3), 1), status.Next);
    }

    [Fact]
    public void Midnight_to_midnight_means_open_the_whole_day()
    {
        HoursInterval[] hours = [new(Sunday, 0, 0)];

        Assert.True(OpeningHours.IsOpen(hours, new WeekTime(Sunday, 0)));
        Assert.True(OpeningHours.IsOpen(hours, new WeekTime(Sunday, At(23, 59))));
        Assert.False(OpeningHours.IsOpen(hours, new WeekTime(Monday, 0)));
    }

    [Fact]
    public void Open_around_the_clock_every_day_has_no_next_change()
    {
        var hours = Enumerable.Range(0, 7).Select(day => new HoursInterval(day, 0, 0)).ToList();

        var status = OpeningHours.Status(hours, new WeekTime(Tuesday, At(4)));

        Assert.True(status.IsOpen);
        Assert.True(status.AlwaysOpen);
        Assert.Null(status.Next);
    }

    [Fact]
    public void Next_change_is_the_closing_time_while_open_and_the_next_opening_while_closed()
    {
        HoursInterval[] hours = [new(Monday, At(9), At(17)), new(Tuesday, At(10), At(14))];

        Assert.Equal(new NextChange("closes", Monday, At(17), 0), OpeningHours.Status(hours, new WeekTime(Monday, At(12))).Next);
        Assert.Equal(new NextChange("opens", Tuesday, At(10), 1), OpeningHours.Status(hours, new WeekTime(Monday, At(18))).Next);
        // After Tuesday closes, the next opening is next Monday: six days ahead.
        Assert.Equal(new NextChange("opens", Monday, At(9), 6), OpeningHours.Status(hours, new WeekTime(Tuesday, At(15))).Next);
    }

    [Fact]
    public void Touching_and_overlapping_intervals_merge_before_the_next_change_is_found()
    {
        HoursInterval[] hours =
        [
            new(Monday, At(9), At(12)),
            new(Monday, At(12), At(15)),
            new(Monday, At(20), At(2)),
            new(Tuesday, At(1), At(4)),
        ];

        Assert.Equal(new NextChange("closes", Monday, At(15), 0), OpeningHours.Status(hours, new WeekTime(Monday, At(11))).Next);
        Assert.Equal(new NextChange("closes", Tuesday, At(4), 0), OpeningHours.Status(hours, new WeekTime(Tuesday, At(1, 30))).Next);
        Assert.Equal(
            [(At(9) + 1440, At(15) + 1440), (At(20) + 1440, At(4) + 2 * 1440)],
            OpeningHours.WeeklyRanges(hours));
    }

    [Fact]
    public void Per_interval_rule_merged_ranges_and_the_query_predicate_always_agree()
    {
        var random = new Random(18);
        for (var trial = 0; trial < 400; trial++)
        {
            var hours = RandomHours(random);
            var place = new Place
            {
                Name = "x",
                Description = "x",
                Locality = "x",
                Address = "x",
                OpeningHours = hours.Select(interval => new PlaceOpeningHours
                {
                    DayOfWeek = (short)interval.Day,
                    OpensMinute = (short)interval.Opens,
                    ClosesMinute = (short)interval.Closes,
                }).ToList(),
            };

            for (var sample = 0; sample < 25; sample++)
            {
                var now = new WeekTime(random.Next(7), random.Next(OpeningHours.MinutesPerDay));
                var expected = OpeningHours.IsOpen(hours, now);

                Assert.Equal(expected, OpeningHours.Status(hours, now).IsOpen);
                Assert.Equal(expected, OpeningHours.OpenAt(now).Compile()(place));
            }
        }
    }

    [Fact]
    public void Status_at_the_next_change_flips_the_open_state()
    {
        var random = new Random(7);
        for (var trial = 0; trial < 300; trial++)
        {
            var hours = RandomHours(random);
            if (hours.Count == 0) continue;
            var now = new WeekTime(random.Next(7), random.Next(OpeningHours.MinutesPerDay));
            var status = OpeningHours.Status(hours, now);
            if (status.AlwaysOpen) continue;

            var change = status.Next!;
            var before = MinusOne(new WeekTime(change.Day, change.Minute));
            Assert.Equal(status.IsOpen, OpeningHours.IsOpen(hours, before));
            Assert.Equal(!status.IsOpen, OpeningHours.IsOpen(hours, new WeekTime(change.Day, change.Minute)));
        }
    }

    [Fact]
    public void Local_time_follows_israel_daylight_saving_time()
    {
        // Daylight saving time started on Friday 27 March 2026 at 02:00 and ends on Sunday 25 October 2026 at 02:00.
        Assert.Equal(new WeekTime(Friday, At(1, 30)), OpeningHours.ToLocal(new DateTimeOffset(2026, 3, 26, 23, 30, 0, TimeSpan.Zero)));
        Assert.Equal(new WeekTime(Friday, At(3, 30)), OpeningHours.ToLocal(new DateTimeOffset(2026, 3, 27, 0, 30, 0, TimeSpan.Zero)));
        // 01:30 happens twice on the night the clocks go back; both are 01:30 on the wall clock.
        Assert.Equal(new WeekTime(Sunday, At(1, 30)), OpeningHours.ToLocal(new DateTimeOffset(2026, 10, 24, 22, 30, 0, TimeSpan.Zero)));
        Assert.Equal(new WeekTime(Sunday, At(1, 30)), OpeningHours.ToLocal(new DateTimeOffset(2026, 10, 24, 23, 30, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Validation_accepts_a_normal_week_and_rejects_overlaps_duplicates_and_zero_length()
    {
        Assert.Empty(OpeningHours.Validate([new(Sunday, 0, 0), new(Monday, At(9), At(13)), new(Monday, At(16), At(2))]));

        Assert.Contains("Opening intervals of the same day cannot overlap.", OpeningHours.Validate([new(Monday, At(9), At(13)), new(Monday, At(12), At(18))]));
        Assert.Contains("A day can have at most 3 opening intervals.", OpeningHours.Validate(
            [new(Monday, At(6), At(7)), new(Monday, At(8), At(9)), new(Monday, At(10), At(11)), new(Monday, At(12), At(13))]));
        Assert.Contains(
            "An interval cannot open and close at the same time; use 00:00–00:00 for 24 hours.",
            OpeningHours.Validate([new(Monday, At(9), At(9))]));
        Assert.Contains("Times must be between 00:00 and 23:59.", OpeningHours.Validate([new(Monday, At(9), 1440)]));
        Assert.Contains("Day of week must be between 0 (Sunday) and 6 (Saturday).", OpeningHours.Validate([new(7, At(9), At(10))]));
    }

    [Fact]
    public void Input_validation_normalises_contact_details_and_reports_each_field()
    {
        var valid = Request() with { Instagram = "@galil.live", Website = "https://galil.example", Phone = "+972 4-690-1234" };
        var input = PlaceInputValidator.Validate(valid, requireRevision: false);
        Assert.Equal("galil.live", input.Instagram);
        Assert.Equal("+972 4-690-1234", input.Phone);

        var invalid = Request() with
        {
            Name = " ",
            Website = "javascript:alert(1)",
            Instagram = "not a handle!",
            Phone = "call me",
            ImageId = Guid.Empty,
            Hours = [new PlaceHoursDto(Monday, At(9), At(13)), new PlaceHoursDto(Monday, At(12), At(14))],
        };
        var errors = Assert.Throws<PlaceValidationException>(() => PlaceInputValidator.Validate(invalid, requireRevision: true)).Errors;
        Assert.Equal(
            ["hours", "imageId", "instagram", "name", "phone", "revision", "website"],
            errors.Keys.Order().ToArray());
    }

    private static OwnerPlaceUpsertRequest Request() => new(
        "גליל לייב", PlaceCategory.Nightlife, "בר הופעות", "קריית שמונה", "תל חי 1", 33.2m, 35.57m,
        null, null, null, "10% הנחה לסטודנטים", Guid.NewGuid(), [new PlaceHoursDto(Thursday, At(20), At(2))], null);

    private static List<HoursInterval> RandomHours(Random random)
    {
        var hours = new List<HoursInterval>();
        for (var day = 0; day < 7; day++)
        {
            var roll = random.Next(10);
            if (roll < 2) continue;
            if (roll == 2)
            {
                hours.Add(new HoursInterval(day, 0, 0));
                continue;
            }

            var count = random.Next(1, 3);
            var cursor = random.Next(0, 600);
            for (var index = 0; index < count && cursor < OpeningHours.MinutesPerDay - 30; index++)
            {
                var opens = cursor;
                var length = random.Next(30, 900);
                var closes = (opens + length) % OpeningHours.MinutesPerDay;
                if (closes == opens) closes = (closes + 1) % OpeningHours.MinutesPerDay;
                hours.Add(new HoursInterval(day, opens, closes));
                if (closes <= opens) break;
                cursor = closes + random.Next(0, 120);
            }
        }

        return hours;
    }

    private static WeekTime MinusOne(WeekTime time) =>
        time.Minute > 0
            ? time with { Minute = time.Minute - 1 }
            : new WeekTime((time.Day + 6) % 7, OpeningHours.MinutesPerDay - 1);
}
