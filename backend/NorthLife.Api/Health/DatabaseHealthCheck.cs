using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NorthLife.Api.Data;

namespace NorthLife.Api.Health;

public sealed class DatabaseHealthCheck(AppDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await dbContext.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("PostgreSQL is reachable.")
                : HealthCheckResult.Unhealthy("PostgreSQL is unavailable.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL is unavailable.", exception);
        }
    }
}
