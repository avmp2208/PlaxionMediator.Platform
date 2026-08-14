using System.Collections.Concurrent;

namespace Platform.Storage;

public sealed class InMemoryServiceCatalog : IServiceCatalog
{
    private readonly ConcurrentDictionary<Guid, Service> _services = new();

    public Task<Service> GetOrCreateAsync(Guid tenantId, string name, string environment, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = ServiceIdGenerator.Compute(tenantId, name, environment);
        var service = _services.GetOrAdd(id, _ => new Service
        {
            Id = id,
            TenantId = tenantId,
            Name = name,
            Environment = environment
        });
        return Task.FromResult(service);
    }

    public Task<IReadOnlyList<Service>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<Service> result = _services.Values.Where(s => s.TenantId == tenantId).ToList();
        return Task.FromResult(result);
    }

    public Task<Service?> GetByIdAsync(Guid tenantId, Guid serviceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _services.TryGetValue(serviceId, out var service);
        if (service is not null && service.TenantId != tenantId)
        {
            // Cross-tenant access attempt: never leak existence of another tenant's service.
            service = null;
        }

        return Task.FromResult(service);
    }
}
