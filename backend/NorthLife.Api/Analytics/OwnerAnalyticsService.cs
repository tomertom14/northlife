using Microsoft.EntityFrameworkCore;
using NorthLife.Api.Data;
using NorthLife.Api.Models;

namespace NorthLife.Api.Analytics;

public sealed record AnalyticsTotals(
    int Impressions,
    int DetailViews,
    int Navigations,
    int Shares,
    long UniqueVisitors,
    double? ClickThroughRate);

public sealed record AnalyticsDay(DateOnly Day, int Impressions, int DetailViews, int Navigations, int Shares, long UniqueVisitors);

public sealed record EventAnalytics(
    Guid Id,
    string Title,
    EventStatus Status,
    DateTimeOffset StartAt,
    int Impressions,
    int DetailViews,
    int Navigations,
    int Shares,
    long UniqueVisitors,
    double? ClickThroughRate,
    bool Trending,
    double? TrendScore);

public sealed record OwnerAnalyticsResponse(
    DateOnly From,
    DateOnly To,
    AnalyticsTotals Totals,
    IReadOnlyList<AnalyticsDay> Daily,
    IReadOnlyList<EventAnalytics> Events);

public sealed record TrafficAnomaly(
    Guid EventId,
    string Title,
    string BusinessName,
    int Observed,
    double Baseline,
    double ZScore,
    string Kind);

/// <summary>Builds the owner analytics page and the administrators' unusual-traffic list.</summary>
public sealed class OwnerAnalyticsService(AppDbContext dbContext, TimeProvider timeProvider)
{
    public static readonly int[] AllowedDays = [7, 30, 90];

    /// <summary>Hours of history the spike detector looks at.</summary>
    public const int TrendHours = 7 * 24;

    public async Task<OwnerAnalyticsResponse> GetAsync(Guid ownerId, int days, CancellationToken cancellationToken)
    {
        if (!AllowedDays.Contains(days)) days = 30;
        var now = timeProvider.GetUtcNow();
        var to = JerusalemDays.Of(now);
        var from = to.AddDays(-(days - 1));

        var events = await dbContext.Events.AsNoTracking()
            .Where(eventItem => eventItem.OwnerId == ownerId && eventItem.DeletedAtUtc == null)
            .OrderByDescending(eventItem => eventItem.StartAtUtc)
            .Select(eventItem => new { eventItem.Id, eventItem.Title, eventItem.Status, eventItem.StartAtUtc, eventItem.EndAtUtc })
            .ToListAsync(cancellationToken);
        var eventIds = events.Select(eventItem => eventItem.Id).ToList();

        var rows = await dbContext.EventStatsDaily.AsNoTracking()
            .Where(stats => eventIds.Contains(stats.EventId) && stats.Day >= from && stats.Day <= to)
            .ToListAsync(cancellationToken);

        // Unique visitors over any set of days or events is a union of sketches, never a sum:
        // someone who opened two of the owner's events, or came back on another day, counts once.
        var daily = new List<AnalyticsDay>(days);
        var rowsByDay = rows.ToLookup(row => row.Day);
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var dayRows = rowsByDay[day].ToList();
            daily.Add(new AnalyticsDay(
                day,
                dayRows.Sum(row => row.Impressions),
                dayRows.Sum(row => row.DetailViews),
                dayRows.Sum(row => row.Navigations),
                dayRows.Sum(row => row.Shares),
                HyperLogLog.Union(dayRows.Select(row => row.VisitorSketch)).Count()));
        }

        var trends = await TrendsAsync(
            events.Where(eventItem => eventItem.Status == EventStatus.Published && eventItem.EndAtUtc > now).Select(eventItem => eventItem.Id).ToList(),
            now,
            cancellationToken);
        var rowsByEvent = rows.ToLookup(row => row.EventId);
        var perEvent = events.Select(eventItem =>
        {
            var eventRows = rowsByEvent[eventItem.Id].ToList();
            var impressions = eventRows.Sum(row => row.Impressions);
            var views = eventRows.Sum(row => row.DetailViews);
            trends.TryGetValue(eventItem.Id, out var trend);
            return new EventAnalytics(
                eventItem.Id,
                eventItem.Title,
                eventItem.Status,
                eventItem.StartAtUtc,
                impressions,
                views,
                eventRows.Sum(row => row.Navigations),
                eventRows.Sum(row => row.Shares),
                HyperLogLog.Union(eventRows.Select(row => row.VisitorSketch)).Count(),
                Rate(views, impressions),
                trend?.IsSpike ?? false,
                trend is null ? null : Math.Round(trend.ZScore, 2));
        }).ToList();

        var totalImpressions = rows.Sum(row => row.Impressions);
        var totalViews = rows.Sum(row => row.DetailViews);
        var totals = new AnalyticsTotals(
            totalImpressions,
            totalViews,
            rows.Sum(row => row.Navigations),
            rows.Sum(row => row.Shares),
            HyperLogLog.Union(rows.Select(row => row.VisitorSketch)).Count(),
            Rate(totalViews, totalImpressions));

        return new OwnerAnalyticsResponse(from, to, totals, daily, perEvent);
    }

    /// <summary>
    /// Current events whose last complete hour is a spike. A spike whose navigation and share rate
    /// collapses to under a quarter of the event's own baseline is marked "suspicious": many detail
    /// views that lead nowhere look like automated traffic rather than people planning to attend.
    /// </summary>
    public async Task<IReadOnlyList<TrafficAnomaly>> AnomaliesAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var (firstHour, lastHour) = TrendWindow(now);
        var candidates = await dbContext.Events.AsNoTracking()
            .Where(eventItem => eventItem.Status == EventStatus.Published && eventItem.DeletedAtUtc == null && eventItem.EndAtUtc > now)
            .Where(eventItem => dbContext.EventStatsHourly.Any(stats => stats.EventId == eventItem.Id && stats.HourUtc == lastHour))
            .Select(eventItem => new { eventItem.Id, eventItem.Title, eventItem.Owner.BusinessName })
            .ToListAsync(cancellationToken);
        if (candidates.Count == 0) return [];

        var ids = candidates.Select(candidate => candidate.Id).ToList();
        var hourly = await dbContext.EventStatsHourly.AsNoTracking()
            .Where(stats => ids.Contains(stats.EventId) && stats.HourUtc >= firstHour && stats.HourUtc <= lastHour)
            .ToListAsync(cancellationToken);
        var byEvent = hourly.ToLookup(stats => stats.EventId);

        var anomalies = new List<TrafficAnomaly>();
        foreach (var candidate in candidates)
        {
            var series = byEvent[candidate.Id].ToList();
            var result = SpikeDetector.Evaluate(Series(series, firstHour, lastHour, stats => stats.DetailViews));
            if (!result.IsSpike) continue;

            var spikeHour = series.Single(stats => stats.HourUtc == lastHour);
            var history = series.Where(stats => stats.HourUtc < lastHour).ToList();
            var historyViews = history.Sum(stats => stats.DetailViews);
            var baselineRate = Rate(history.Sum(stats => stats.Navigations + stats.Shares), historyViews);
            var spikeRate = Rate(spikeHour.Navigations + spikeHour.Shares, spikeHour.DetailViews) ?? 0;
            var suspicious = historyViews >= 20 && baselineRate is > 0 && spikeRate < baselineRate.Value / 4;
            anomalies.Add(new TrafficAnomaly(
                candidate.Id,
                candidate.Title,
                candidate.BusinessName,
                result.Observed,
                Math.Round(result.Baseline, 2),
                Math.Round(result.ZScore, 2),
                suspicious ? "suspicious" : "surge"));
        }

        return anomalies.OrderByDescending(anomaly => anomaly.ZScore).ToList();
    }

    private async Task<Dictionary<Guid, SpikeResult>> TrendsAsync(List<Guid> eventIds, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (eventIds.Count == 0) return [];
        var (firstHour, lastHour) = TrendWindow(now);
        var hourly = await dbContext.EventStatsHourly.AsNoTracking()
            .Where(stats => eventIds.Contains(stats.EventId) && stats.HourUtc >= firstHour && stats.HourUtc <= lastHour)
            .ToListAsync(cancellationToken);
        var byEvent = hourly.ToLookup(stats => stats.EventId);
        return eventIds.ToDictionary(
            id => id,
            id => SpikeDetector.Evaluate(Series(byEvent[id], firstHour, lastHour, stats => stats.DetailViews)));
    }

    /// <summary>The last <see cref="TrendHours"/> complete UTC hours.</summary>
    private static (DateTimeOffset First, DateTimeOffset Last) TrendWindow(DateTimeOffset now)
    {
        var utc = now.UtcDateTime;
        var currentHour = new DateTimeOffset(utc.Date.AddHours(utc.Hour), TimeSpan.Zero);
        var last = currentHour.AddHours(-1);
        return (last.AddHours(-(TrendHours - 1)), last);
    }

    /// <summary>Zero-filled hourly series: hours without a row had no interactions.</summary>
    public static int[] Series(IEnumerable<EventStatsHourly> rows, DateTimeOffset firstHour, DateTimeOffset lastHour, Func<EventStatsHourly, int> value)
    {
        var length = (int)(lastHour - firstHour).TotalHours + 1;
        var series = new int[length];
        foreach (var row in rows)
        {
            var index = (int)(row.HourUtc - firstHour).TotalHours;
            if (index >= 0 && index < length) series[index] = value(row);
        }

        return series;
    }

    private static double? Rate(int part, int whole) => whole == 0 ? null : Math.Round((double)part / whole, 4);
}
