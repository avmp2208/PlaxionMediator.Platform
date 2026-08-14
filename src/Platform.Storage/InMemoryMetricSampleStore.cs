using System.Collections.Concurrent;

namespace Platform.Storage;

/// <summary>
/// In-memory implementation retained for tests and local Development.
/// Durable providers (e.g. SQLite) live in separate packages and implement
/// the same interfaces.
/// </summary>
public sealed class InMemoryMetricSampleStore : IMetricSampleStore
{
    private readonly ConcurrentBag<MetricSample> _samples = new();

    public Task AddAsync(MetricSample sample, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _samples.Add(sample);
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
        IEnumerable<MetricSample> query = _samples.Where(s => s.TenantId == tenantId);
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
        IReadOnlyList<MetricSample> result = _samples.ToList();
        return Task.FromResult(result);
    }
}
