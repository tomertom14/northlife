using NorthLife.Api.Services;

namespace NorthLife.Tests;

public sealed class EventTimeWindowFactoryTests
{
    private readonly EventTimeWindowFactory _factory = new();

    [Fact]
    public void Today_uses_Jerusalem_daylight_saving_boundaries()
    {
        var now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        var window = _factory.Today(now);

        Assert.Equal(
            new DateTimeOffset(2026, 5, 31, 21, 0, 0, TimeSpan.Zero),
            window.Start);
        Assert.Equal(
            new DateTimeOffset(2026, 6, 1, 21, 0, 0, TimeSpan.Zero),
            window.End);
    }

    [Fact]
    public void Today_uses_Jerusalem_winter_boundaries()
    {
        var now = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

        var window = _factory.Today(now);

        Assert.Equal(
            new DateTimeOffset(2026, 1, 14, 22, 0, 0, TimeSpan.Zero),
            window.Start);
        Assert.Equal(
            new DateTimeOffset(2026, 1, 15, 22, 0, 0, TimeSpan.Zero),
            window.End);
    }

    [Fact]
    public void Tonight_runs_from_six_pm_to_six_am_in_Jerusalem()
    {
        var now = new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

        var window = _factory.Tonight(now);

        Assert.Equal(
            new DateTimeOffset(2026, 9, 22, 15, 0, 0, TimeSpan.Zero),
            window.Start);
        Assert.Equal(
            new DateTimeOffset(2026, 9, 23, 3, 0, 0, TimeSpan.Zero),
            window.End);
    }

    [Fact]
    public void Date_range_includes_the_complete_end_date()
    {
        var window = _factory.LocalDateRange(
            new DateOnly(2026, 9, 22),
            new DateOnly(2026, 9, 24));

        Assert.Equal(
            new DateTimeOffset(2026, 9, 21, 21, 0, 0, TimeSpan.Zero),
            window.Start);
        Assert.Equal(
            new DateTimeOffset(2026, 9, 24, 21, 0, 0, TimeSpan.Zero),
            window.End);
    }

    [Fact]
    public void Reversed_date_range_is_rejected()
    {
        var exception = Assert.Throws<PublicEventQueryValidationException>(
            () => _factory.LocalDateRange(
                new DateOnly(2026, 9, 24),
                new DateOnly(2026, 9, 22)));

        Assert.Equal("to", exception.Field);
    }
}
