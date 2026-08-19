namespace Platform.Storage;

public interface IAggregateRollupStore
{
    Task AddRangeAsync(IEnumerable<AggregateRollup> rollups, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AggregateRollup>> QueryAsync(
        Guid tenantId,
        Guid serviceId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
