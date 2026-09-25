using Microsoft.Extensions.Options;

namespace NorthLife.Api.Analytics;

/// <summary>
/// Runs inside the web process: partition upkeep at start-up and every six hours, and the rollup
/// every minute. (A separate cron service could not share the web service's disk on Render, and the
/// work is small.)
/// </summary>
public sealed class AnalyticsWorker(
    IServiceScopeFactory scopes,
    IOptions<AnalyticsOptions> options,
    AnalyticsMetrics metrics,
    TimeProvider timeProvider,
    ILogger<AnalyticsWorker> logger) : BackgroundService
{
    private static readonly TimeSpan MaintenanceInterval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var interval = TimeSpan.FromSeconds(Math.Max(5, settings.RollupIntervalSeconds));
        var lag = TimeSpan.FromSeconds(Math.Max(0, settings.IngestLagSeconds));
        DateTimeOffset? lastMaintenance = null;

        using var timer = new PeriodicTimer(interval, timeProvider);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var rollup = scope.ServiceProvider.GetRequiredService<AnalyticsRollupService>();
                var positionBias = scope.ServiceProvider.GetRequiredService<Ranking.PositionBiasService>();

                var now = timeProvider.GetUtcNow();
                if (lastMaintenance is null || now - lastMaintenance >= MaintenanceInterval)
                {
                    var (created, dropped) = await rollup.MaintainPartitionsAsync(settings.RawRetentionDays, stoppingToken);
                    var estimate = await positionBias.EstimateAsync(stoppingToken);
                    lastMaintenance = now;
                    if (created > 0 || dropped > 0)
                    {
                        logger.LogInformation("Interaction partitions: {Created} created, {Dropped} dropped.", created, dropped);
                    }

                    if (estimate.Count > 0)
                    {
                        logger.LogInformation("Position bias re-estimated for {Positions} feed positions.", estimate.Count);
                    }
                }

                rollup.Weight = Ranking.PopularityWeights.With(await positionBias.LoadAsync(stoppingToken));
                var processed = await rollup.RunAsync(lag, stoppingToken);
                metrics.CountRollup("success");
                if (processed > 0) logger.LogInformation("Rolled up {InteractionCount} interactions.", processed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                metrics.CountRollup("failure");
                logger.LogError(exception, "Analytics rollup failed; it will retry on the next tick.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
