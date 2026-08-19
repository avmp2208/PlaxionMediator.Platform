using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Platform.Storage.Relational.Health;

public static class PlatformStorageHealthChecksExtensions
{
    /// <summary>
    /// Registers readiness health checks for platform storage.
    /// When a relational <see cref="IDbContextFactory{PlatformDbContext}"/> is present,
    /// DB connectivity is checked; otherwise the check is always healthy (InMemory).
    /// </summary>
    public static IHealthChecksBuilder AddPlatformStorageHealthChecks(this IHealthChecksBuilder builder)
    {
        return builder.Add(new HealthCheckRegistration(
            name: "database",
            factory: sp =>
            {
                var dbFactory = sp.GetService<IDbContextFactory<PlatformDbContext>>();
                return dbFactory is null
                    ? new AlwaysHealthyStorageCheck()
                    : new PlatformDbContextHealthCheck(dbFactory);
            },
            failureStatus: HealthStatus.Unhealthy,
            tags: ["ready"]));
    }
}

/// <summary>Used when InMemory storage is active (no external database).</summary>
public sealed class AlwaysHealthyStorageCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(HealthCheckResult.Healthy("In-memory storage; no external DB."));
}
