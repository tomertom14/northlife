namespace NorthLife.Api.Moderation;

/// <summary>
/// When the daily run happens. Times are Israel local time; the zone rules handle daylight saving.
/// A run time that falls in the spring-forward gap (for example 02:30 on the Friday the clocks jump
/// from 02:00 to 03:00) moves one hour later. An ambiguous autumn time resolves to standard time.
/// </summary>
public static class AutoModerationSchedule
{
    public static readonly TimeZoneInfo Israel = TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem");

    public static DateOnly LocalDate(DateTimeOffset utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, Israel).DateTime);

    /// <summary>The UTC instant of the run on an Israel date.</summary>
    public static DateTimeOffset RunInstant(DateOnly date, TimeOnly runAt)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(runAt), DateTimeKind.Unspecified);
        if (Israel.IsInvalidTime(local)) local = local.AddHours(1);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, Israel));
    }

    /// <summary>
    /// True once today's run time has passed while today's run is not yet claimed. Because the worker
    /// asks every few minutes, a run missed while the app was down happens at the first check after it
    /// starts again that day.
    /// </summary>
    public static bool IsDue(DateTimeOffset now, TimeOnly runAt, DateOnly? lastScheduledRunDate)
    {
        var today = LocalDate(now);
        return (lastScheduledRunDate is null || lastScheduledRunDate < today) && now >= RunInstant(today, runAt);
    }

    /// <summary>When the next daily run happens: later today, now if it is due, or tomorrow once today's ran.</summary>
    public static DateTimeOffset NextRun(DateTimeOffset now, TimeOnly runAt, DateOnly? lastScheduledRunDate)
    {
        var today = LocalDate(now);
        if (lastScheduledRunDate is null || lastScheduledRunDate < today)
        {
            var todays = RunInstant(today, runAt);
            return todays > now ? todays : now;
        }

        return RunInstant(today.AddDays(1), runAt);
    }
}
