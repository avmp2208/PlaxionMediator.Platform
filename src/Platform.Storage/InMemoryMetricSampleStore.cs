using System.Collections.Concurrent;

namespace Platform.Storage;

/// <summary>
/// In-memory implementation retained for tests and local Development.
/// Durable providers (e.g. SQLite) live in separate packages and implement
/// the same interfaces.
/// </summary>
public sealed class InMemoryMetricSampleStore : IMetricSampleStore
{
    // Keyed by sample Id (rather than a ConcurrentBag) so retention eviction
    // (DeleteOlderThanAsync) can remove individual entries without rebuilding
    // the whole collection under a lock.
    private readonly ConcurrentDictionary<Guid, MetricSample> _samples = new();

    public Task AddAsync(MetricSample sample, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _samples[sample.Id] = sample;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MetricSample>> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        QueryAsync(tenantId, from: null, to: null, cancellationToken);

    public Task<IReadOnlyList<MetricSample>> QueryAsync(
        Guid tenantId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IEnumerable<MetricSample> query = _samples.Values.Where(s => s.TenantId == tenantId);
        if (from is not null)
        {
            query = query.Where(s => s.Timestamp >= from.Value);
        }

        if (to is not null)
        {
            query = query.Where(s => s.Timestamp <= to.Value);
        }

        IReadOnlyList<MetricSample> result = query.OrderBy(s => s.Timestamp).ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<MetricSample>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<MetricSample> result = _samples.Values.ToList();
        return Task.FromResult(result);
    }

    public Task DeleteOlderThanAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var sample in _samples.Values)
        {
            if (sample.Timestamp < threshold)
            {
                _samples.TryRemove(sample.Id, out _);
            }
        }

        return Task.CompletedTask;
    }
}
