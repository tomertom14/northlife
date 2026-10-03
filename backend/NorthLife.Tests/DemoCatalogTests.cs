using NorthLife.Api.Analytics;
using NorthLife.Api.Data;

namespace NorthLife.Tests;

public sealed class DemoCatalogTests
{
    // 00:30 in Israel on 3 October 2026, before any catalogue event of the day can have ended.
    private static readonly DateTimeOffset EarlyMorning = new(2026, 10, 2, 21, 30, 0, TimeSpan.Zero);

    [Fact]
    public void The_same_moment_gives_the_same_schedule()
    {
        Assert.Equal(DemoCatalog.Schedule(EarlyMorning), DemoCatalog.Schedule(EarlyMorning));
    }

    [Fact]
    public void Titles_are_unique_because_the_refresh_matches_events_by_title()
    {
        Assert.Equal(DemoCatalog.Events.Length, DemoCatalog.Events.Select(spec => spec.Title).Distinct().Count());
    }

    [Theory]
    [InlineData("2026-10-03T05:00:00Z")]
    [InlineData("2026-10-03T21:45:00Z")]
    [InlineData("2026-10-24T20:30:00Z")] // the evening before daylight saving time ends
    [InlineData("2027-03-25T22:30:00Z")] // the night daylight saving time starts
    public void Every_event_is_still_ahead_and_within_the_coming_two_weeks(string instant)
    {
        var now = DateTimeOffset.Parse(instant);
        var horizon = JerusalemDays.StartUtc(JerusalemDays.Of(now).AddDays(DemoCatalog.ScheduleDays));

        var plan = DemoCatalog.Schedule(now);

        Assert.Equal(DemoCatalog.Events.Length, plan.Count);
        Assert.All(plan, item =>
        {
            Assert.True(item.EndAtUtc > now, $"{item.Spec.Title} is already over");
            Assert.True(item.StartAtUtc < horizon, $"{item.Spec.Title} starts after the two weeks");
            Assert.True(item.EndAtUtc > item.StartAtUtc);
        });
    }

    [Fact]
    public void Every_category_has_an_event_today()
    {
        var today = JerusalemDays.Of(EarlyMorning);

        var plan = DemoCatalog.Schedule(EarlyMorning);

        var categoriesToday = plan.Where(item => JerusalemDays.Of(item.StartAtUtc) == today).Select(item => item.Spec.Category).ToHashSet();
        Assert.True(DemoCatalog.Events.Select(spec => spec.Category).ToHashSet().SetEquals(categoriesToday));
    }

    [Fact]
    public void Only_the_dates_follow_the_moment_of_seeding()
    {
        var later = EarlyMorning.AddDays(10);

        var first = DemoCatalog.Schedule(EarlyMorning);
        var second = DemoCatalog.Schedule(later);

        Assert.All(first.Zip(second), pair =>
        {
            var (before, after) = pair;
            Assert.Equal(
                (before.Spec, before.Locality, before.Venue, before.Price, before.Latitude, before.Longitude, before.IsHighlighted),
                (after.Spec, after.Locality, after.Venue, after.Price, after.Latitude, after.Longitude, after.IsHighlighted));
            Assert.Equal(JerusalemDays.Of(before.StartAtUtc).AddDays(10), JerusalemDays.Of(after.StartAtUtc));
            Assert.Equal(TimeOfDay(before.StartAtUtc), TimeOfDay(after.StartAtUtc));
            Assert.Equal(before.EndAtUtc - before.StartAtUtc, after.EndAtUtc - after.StartAtUtc);
        });
    }

    private static TimeSpan TimeOfDay(DateTimeOffset instant) => instant - JerusalemDays.StartUtc(JerusalemDays.Of(instant));
}
