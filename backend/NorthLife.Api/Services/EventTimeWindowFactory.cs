namespace NorthLife.Api.Services;

public sealed class EventTimeWindowFactory
{
    public const int MaximumRangeDays = 366;
    private static readonly DateOnly EarliestDate = new(2000, 1, 1);
    private static readonly DateOnly LatestDate = new(2100, 12, 31);
    private static readonly TimeZoneInfo Jerusalem =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem");

    public UtcEventWindow Today(DateTimeOffset utcNow)
    {
        var localDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(utcNow, Jerusalem).DateTime);
        return LocalDateRange(localDate, localDate);
    }

    public UtcEventWindow Tomorrow(DateTimeOffset utcNow)
    {
        var localDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(utcNow, Jerusalem).DateTime).AddDays(1);
        return LocalDateRange(localDate, localDate);
    }

    public UtcEventWindow Tonight(DateTimeOffset utcNow)
    {
        var localDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(utcNow, Jerusalem).DateTime);
        var start = LocalToUtc(localDate, new TimeOnly(18, 0));
        var end = LocalToUtc(localDate.AddDays(1), new TimeOnly(6, 0));
        return new UtcEventWindow(start, end);
    }

    public UtcEventWindow LocalDateRange(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            throw new PublicEventQueryValidationException(
                "to",
                "The end date must be on or after the start date.");
        }

        if (from < EarliestDate || to > LatestDate)
        {
            throw new PublicEventQueryValidationException(
                "from",
                $"Dates must be between {EarliestDate:yyyy-MM-dd} and {LatestDate:yyyy-MM-dd}.");
        }

        if (to.DayNumber - from.DayNumber > MaximumRangeDays)
        {
            throw new PublicEventQueryValidationException(
                "to",
                $"A date range cannot exceed {MaximumRangeDays} days.");
        }

        return new UtcEventWindow(
            LocalToUtc(from, TimeOnly.MinValue),
            LocalToUtc(to.AddDays(1), TimeOnly.MinValue));
    }

    private static DateTimeOffset LocalToUtc(DateOnly date, TimeOnly time)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(local, Jerusalem);
        return new DateTimeOffset(utc);
    }
}
