namespace NorthLife.Api.Services;

public sealed class EventTimeWindowFactory
{
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
