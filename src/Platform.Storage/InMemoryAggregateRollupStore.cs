namespace Platform.Storage;

public sealed class InMemoryAggregateRollupStore : IAggregateRollupStore
{
    private readonly object _lock = new();
    private readonly Dictionary<RollupKey, (Guid ServiceId, AggregateRollup Rollup)> _rollups = new();

    public Task AddRangeAsync(IEnumerable<AggregateRollup> rollups, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Mirrors the relational store's upsert behavior: the aggregation job
        // recomputes every bucket still in the retention window on each run,
        // so replacing an existing entry for the same key (instead of always
        // adding) prevents unbounded duplicate growth while the job is left
        // running.
        lock (_lock)
        {
            foreach (var rollup in rollups)
            {
                var serviceId = ServiceCatalogKey(rollup);
                var key = new RollupKey(rollup.TenantId, serviceId, rollup.MetricName, rollup.RequestType, rollup.BucketStart);
                _rollups[key] = (serviceId, rollup);
            }
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
        IReadOnlyList<AggregateRollup> result;
        lock (_lock)
        {
            result = _rollups.Values
                .Where(r => r.ServiceId == serviceId
                            && r.Rollup.TenantId == tenantId
                            && r.Rollup.BucketStart >= from
                            && r.Rollup.BucketStart <= to)
                .Select(r => r.Rollup)
                .OrderBy(r => r.BucketStart)
                .ToList();
        }

        return Task.FromResult(result);
    }

    private readonly record struct RollupKey(Guid TenantId, Guid ServiceId, string MetricName, string? RequestType, DateTimeOffset BucketStart);
}
