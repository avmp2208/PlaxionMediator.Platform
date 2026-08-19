using Platform.Storage;
using Xunit;

namespace Platform.Storage.Tests;

public class ServiceCatalogTests
{
    [Fact]
    public async Task GetOrCreate_SameKey_ReturnsSameServiceId()
    {
        var catalog = new InMemoryServiceCatalog();
        var tenantId = Guid.NewGuid();

        var first = await catalog.GetOrCreateAsync(tenantId, "checkout-api", "prod");
        var second = await catalog.GetOrCreateAsync(tenantId, "checkout-api", "prod");

        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task GetById_CrossTenantAccess_ReturnsNull()
    {
        var catalog = new InMemoryServiceCatalog();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var service = await catalog.GetOrCreateAsync(tenantA, "checkout-api", "prod");

        var resultForOwner = await catalog.GetByIdAsync(tenantA, service.Id);
        var resultForOtherTenant = await catalog.GetByIdAsync(tenantB, service.Id);

        Assert.NotNull(resultForOwner);
        Assert.Null(resultForOtherTenant);
    }

    [Fact]
    public async Task ListByTenant_OnlyReturnsOwnServices()
    {
        var catalog = new InMemoryServiceCatalog();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await catalog.GetOrCreateAsync(tenantA, "service-a", "prod");
        await catalog.GetOrCreateAsync(tenantB, "service-b", "prod");

        var tenantAServices = await catalog.ListByTenantAsync(tenantA);

        var single = Assert.Single(tenantAServices);
        Assert.Equal("service-a", single.Name);
    }
}
