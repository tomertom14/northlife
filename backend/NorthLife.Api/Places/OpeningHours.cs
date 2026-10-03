using System.Linq.Expressions;
using NorthLife.Api.Models;

namespace NorthLife.Api.Places;

/// <summary>One opening interval: day of week (0 = Sunday) and minutes from midnight, Israel local time.</summary>
public readonly record struct HoursInterval(int Day, int Opens, int Closes)
{
    /// <summary>A closing time at or before the opening time means the next day; 00:00–00:00 is 24 hours.</summary>
    public bool Overnight => Closes <= Opens;

    public int Length => Overnight ? OpeningHours.MinutesPerDay - Opens + Closes : Closes - Opens;
}

/// <summary>A moment as Israel wall-clock time within the week.</summary>
public readonly record struct WeekTime(int Day, int Minute)
{
    public int Absolute => Day * OpeningHours.MinutesPerDay + Minute;
}

/// <summary>The next moment the place opens or closes, as a day of week and minute, plus how many days ahead.</summary>
public sealed record NextChange(string Kind, int Day, int Minute, int DaysAhead);

public sealed record OpenStatus(bool IsOpen, bool AlwaysOpen, NextChange? Next);

/// <summary>
/// "Open now" for weekly opening hours, including places that close after midnight and places open around the clock.
/// The same rule exists three times: <see cref="IsOpen"/> (per interval, for tests and labels), <see cref="OpenAt"/>
/// (the same predicate as an expression that Entity Framework translates to SQL, so it filters in the database and
/// composes with the geohash nearest-neighbour search) and <see cref="Status"/> (merged weekly ranges, for the next change).
/// </summary>
public static class OpeningHours
{
    public const int MinutesPerDay = 24 * 60;
    public const int MinutesPerWeek = 7 * MinutesPerDay;
    public const int MaxIntervalsPerDay = 3;

    private static readonly TimeZoneInfo Jerusalem = TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem");

    /// <summary>
    /// Israel wall-clock time of an instant. Daylight saving time needs no special case: opening hours are wall-clock
    /// times, so converting the instant to local time first is all that is required.
    /// </summary>
    public static WeekTime ToLocal(DateTimeOffset instant)
    {
        var local = TimeZoneInfo.ConvertTime(instant, Jerusalem);
        return new WeekTime((int)local.DayOfWeek, local.Hour * 60 + local.Minute);
    }

    /// <summary>
    /// Open at local time (d, t) when an interval of day d has opens ≤ t and either closes later that day or runs
    /// past midnight, or when an overnight interval of day d − 1 has not closed yet (t &lt; closes). O(k).
    /// </summary>
    public static bool IsOpen(IEnumerable<HoursInterval> hours, WeekTime now)
    {
        var previous = (now.Day + 6) % 7;
        foreach (var interval in hours)
        {
            if (interval.Day == now.Day && interval.Opens <= now.Minute && (interval.Overnight || now.Minute < interval.Closes))
            {
                return true;
            }

            if (interval.Day == previous && interval.Overnight && now.Minute < interval.Closes)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The <see cref="IsOpen"/> rule as a query predicate; PostgreSQL evaluates it as an EXISTS subquery.</summary>
    public static Expression<Func<Place, bool>> OpenAt(WeekTime now)
    {
        var day = (short)now.Day;
        var previous = (short)((now.Day + 6) % 7);
        var minute = (short)now.Minute;
        return place => place.OpeningHours.Any(hours =>
            (hours.DayOfWeek == day && hours.OpensMinute <= minute &&
                (hours.ClosesMinute <= hours.OpensMinute || minute < hours.ClosesMinute)) ||
            (hours.DayOfWeek == previous && hours.ClosesMinute <= hours.OpensMinute && minute < hours.ClosesMinute));
    }

    /// <summary>
    /// Maps the intervals onto a circular week of 10,080 minutes (an overnight interval runs into the next day, and
    /// Saturday night wraps to Sunday morning) and merges overlapping or touching ranges by sort and sweep.
    /// O(k log k). The result is sorted, disjoint and within [0, 10080).
    /// </summary>
    public static List<(int Start, int End)> WeeklyRanges(IEnumerable<HoursInterval> hours)
    {
        var pieces = new List<(int Start, int End)>();
        foreach (var interval in hours)
        {
            var start = interval.Day * MinutesPerDay + interval.Opens;
            var end = start + interval.Length;
            if (end <= MinutesPerWeek)
            {
                pieces.Add((start, end));
            }
            else
            {
                pieces.Add((start, MinutesPerWeek));
                pieces.Add((0, end - MinutesPerWeek));
            }
        }

        pieces.Sort();
        var merged = new List<(int Start, int End)>();
        foreach (var piece in pieces)
        {
            if (merged.Count > 0 && piece.Start <= merged[^1].End)
            {
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, piece.End));
            }
            else
            {
                merged.Add(piece);
            }
        }

        return merged;
    }

    /// <summary>Whether the place is open, whether it never closes, and the next opening or closing moment.</summary>
    public static OpenStatus Status(IEnumerable<HoursInterval> hours, WeekTime now)
    {
        var ranges = WeeklyRanges(hours);
        if (ranges.Count == 0) return new OpenStatus(false, false, null);
        if (ranges.Count == 1 && ranges[0] == (0, MinutesPerWeek)) return new OpenStatus(true, true, null);

        var t = now.Absolute;
        // A range that ends at the week boundary continues into one that starts there (Saturday night into Sunday).
        var wraps = ranges[0].Start == 0 && ranges[^1].End == MinutesPerWeek;
        foreach (var (start, end) in ranges)
        {
            if (start > t || t >= end) continue;
            var closes = end == MinutesPerWeek && wraps ? MinutesPerWeek + ranges[0].End : end;
            return new OpenStatus(true, false, Change("closes", closes, t));
        }

        // Closed. Nothing opening later this week means the first opening of next week. (With a wrapping range this
        // cannot happen: the Saturday-night range starts after any closed moment.)
        var later = ranges.Where(range => range.Start > t).Select(range => range.Start).ToList();
        var nextStart = later.Count > 0 ? later.Min() : ranges[0].Start + MinutesPerWeek;
        return new OpenStatus(false, false, Change("opens", nextStart, t));
    }

    /// <summary>
    /// Checks one day's intervals: at most three, minutes in range, no zero-length interval except 00:00–00:00
    /// (24 hours), and no overlap between intervals of the same day.
    /// </summary>
    public static IReadOnlyList<string> Validate(IReadOnlyCollection<HoursInterval> hours)
    {
        var errors = new List<string>();
        foreach (var interval in hours)
        {
            if (interval.Day is < 0 or > 6) errors.Add("Day of week must be between 0 (Sunday) and 6 (Saturday).");
            if (interval.Opens is < 0 or >= MinutesPerDay || interval.Closes is < 0 or >= MinutesPerDay)
            {
                errors.Add("Times must be between 00:00 and 23:59.");
            }
            else if (interval.Opens == interval.Closes && interval.Opens != 0)
            {
                errors.Add("An interval cannot open and close at the same time; use 00:00–00:00 for 24 hours.");
            }
        }

        if (errors.Count > 0) return errors.Distinct().ToList();

        foreach (var day in hours.GroupBy(interval => interval.Day))
        {
            if (day.Count() > MaxIntervalsPerDay)
            {
                errors.Add($"A day can have at most {MaxIntervalsPerDay} opening intervals.");
                continue;
            }

            var ordered = day.OrderBy(interval => interval.Opens).ToList();
            for (var index = 1; index < ordered.Count; index++)
            {
                if (ordered[index].Opens < ordered[index - 1].Opens + ordered[index - 1].Length)
                {
                    errors.Add("Opening intervals of the same day cannot overlap.");
                    break;
                }
            }
        }

        return errors.Distinct().ToList();
    }

    private static NextChange Change(string kind, int absolute, int now)
    {
        var day = absolute / MinutesPerDay;
        return new NextChange(kind, day % 7, absolute % MinutesPerDay, day - now / MinutesPerDay);
    }
}
