using Microsoft.Extensions.Options;

namespace NorthLife.Api.Moderation;

/// <summary>
/// Checks every few minutes whether the daily automatic approval run is due and starts it. Checking
/// often, instead of sleeping until the run time, means a run missed while the app was down (Render
/// restarts and sleeps services) happens as soon as the app is back, and a new run time set by an
/// administrator takes effect without a restart. Runs inside the web process like the analytics worker.
/// </summary>
public sealed class AutoModerationWorker(
    IServiceScopeFactory scopes,
    IOptions<AutoModerationOptions> options,
    TimeProvider timeProvider,
    ILogger<AutoModerationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, options.Value.TickSeconds)), timeProvider);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AutoModerationService>().RunScheduledIfDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Automatic event approval failed; it will retry on the next check.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
