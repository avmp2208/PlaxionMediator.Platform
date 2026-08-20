using Microsoft.Extensions.DependencyInjection;

namespace Platform.Storage;

public static class StorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers the in-memory storage providers. Intended for Development
    /// and automated tests. Production hosts should call the SQLite (or
    /// other durable provider) registration instead.
    /// </summary>
    public static IServiceCollection AddPlatformInMemoryStorage(this IServiceCollection services)
    {
        services.AddSingleton<StorageMetrics>();
        services.AddSingleton<IMetricSampleStore, InMemoryMetricSampleStore>();
        services.AddSingleton<IAggregateRollupStore, InMemoryAggregateRollupStore>();
        services.AddSingleton<IServiceCatalog, InMemoryServiceCatalog>();
        services.AddSingleton<IApiKeyStore, InMemoryApiKeyStore>();
        services.AddSingleton<ITenantMembershipStore, InMemoryTenantMembershipStore>();
        services.AddSingleton<ITenantInvitationStore, InMemoryTenantInvitationStore>();
        return services;
    }
}
