using System.Collections.Concurrent;

namespace Platform.Storage;

public sealed class InMemoryAggregateRollupStore : IAggregateRollupStore
{
    private readonly ConcurrentBag<(Guid ServiceId, AggregateRollup Rollup)> _rollups = new();

    public Task AddRangeAsync(IEnumerable<AggregateRollup> rollups, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var rollup in rollups)
        {
            _rollups.Add((ServiceCatalogKey(rollup), rollup));
        }

        return Task.CompletedTask;
    }

    private static Guid ServiceCatalogKey(AggregateRollup rollup) =>
        ServiceIdGenerator.Compute(rollup.TenantId, rollup.ServiceName, rollup.Environment);

    public Task<IReadOnlyList<AggregateRollup>> QueryAsync(
        Guid tenantId,
        Guid serviceId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<AggregateRollup> result = _rollups
            .Where(r => r.ServiceId == serviceId
                        && r.Rollup.TenantId == tenantId
                        && r.Rollup.BucketStart >= from
                        && r.Rollup.BucketStart <= to)
            .Select(r => r.Rollup)
            .OrderBy(r => r.BucketStart)
            .ToList();
        return Task.FromResult(result);
    }
}
