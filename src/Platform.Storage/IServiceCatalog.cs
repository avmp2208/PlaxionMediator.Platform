namespace Platform.Storage;

public interface IServiceCatalog
{
    Task<Service> GetOrCreateAsync(Guid tenantId, string name, string environment, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Service>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<Service?> GetByIdAsync(Guid tenantId, Guid serviceId, CancellationToken cancellationToken = default);
}
