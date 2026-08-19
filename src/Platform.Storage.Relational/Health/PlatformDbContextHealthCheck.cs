using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Platform.Storage.Relational.Health;

/// <summary>
/// Readiness health check that verifies the relational database is reachable.
/// Does not return connection strings or other secrets.
/// </summary>
public sealed class PlatformDbContextHealthCheck : IHealthCheck
{
    private readonly IDbContextFactory<PlatformDbContext> _dbFactory;

    public PlatformDbContextHealthCheck(IDbContextFactory<PlatformDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var canConnect = await db.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy("Database is reachable.")
                : HealthCheckResult.Unhealthy("Database is unreachable.");
        }
        catch (Exception ex)
        {
            // Message only — no connection string details.
            return HealthCheckResult.Unhealthy("Database health check failed.", ex);
        }
    }
}
