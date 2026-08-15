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

    /// <summary>
    /// Deletes every raw MetricSample with <see cref="MetricSample.Timestamp"/>
    /// strictly older than <paramref name="threshold"/>, across all tenants.
    /// Restricted to trusted, system-internal callers only (e.g. the
    /// Platform.Aggregation scheduled retention job) - never exposed through
    /// Platform.Api. Mirrors the trust boundary already established by
    /// <see cref="GetAllAsync"/>: rollups already computed from these samples
    /// (AggregateRollup rows) are untouched, and tenant boundaries are
    /// preserved because deletion is scoped purely by timestamp.
    /// </summary>
    Task DeleteOlderThanAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default);
}
