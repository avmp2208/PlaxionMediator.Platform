namespace Platform.Storage;

public interface IMetricSampleStore
{
    Task AddAsync(MetricSample sample, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MetricSample>> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tenant-scoped query with optional time-range filtering on
    /// <see cref="MetricSample.Timestamp"/>.
    /// </summary>
    Task<IReadOnlyList<MetricSample>> QueryAsync(
        Guid tenantId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every sample regardless of tenant. Restricted to trusted,
    /// system-internal callers only (e.g. the Platform.Aggregation
    /// scheduled job) - never exposed through Platform.Api.
    /// </summary>
    Task<IReadOnlyList<MetricSample>> GetAllAsync(CancellationToken cancellationToken = default);
}
