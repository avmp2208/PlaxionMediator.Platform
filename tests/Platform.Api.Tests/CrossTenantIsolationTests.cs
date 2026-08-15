using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Platform.Api;
using Platform.Identity;
using Platform.Storage;
using Xunit;

namespace Platform.Api.Tests;

/// <summary>
/// Critical security-gate tests: tenant A writes/aggregates data, tenant
/// B's API key must get zero access to tenant A's services/metrics.
/// </summary>
public class CrossTenantIsolationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CrossTenantIsolationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, Guid TenantId, Service Service)> SeedTenantWithServiceAsync(string serviceName)
    {
        using var scope = _factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<ApiKeyService>();
        var serviceCatalog = scope.ServiceProvider.GetRequiredService<IServiceCatalog>();
        var rollupStore = scope.ServiceProvider.GetRequiredService<IAggregateRollupStore>();

        var tenantId = Guid.NewGuid();
        var created = await apiKeyService.CreateAsync(tenantId, "test-key");
        var service = await serviceCatalog.GetOrCreateAsync(tenantId, serviceName, "prod");

        var now = DateTimeOffset.UtcNow;
        await rollupStore.AddRangeAsync(new[]
        {
            new AggregateRollup
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ServiceName = serviceName,
                Environment = "prod",
                MetricName = "http.request.duration",
                BucketStart = now.AddMinutes(-1),
                BucketEnd = now,
                Count = 1,
                Min = 1,
                Max = 1,
                Avg = 1,
                P50 = 1,
                P95 = 1,
                P99 = 1
            }
        });

        var client = _factory.CreateClient();
        return (client, tenantId, service);
    }

    [Fact]
    public async Task GetServices_OnlyReturnsOwnTenantServices()
    {
        var (clientA, tenantA, serviceA) = await SeedTenantWithServiceAsync("service-a");
        var (clientB, tenantB, _) = await SeedTenantWithServiceAsync("service-b");

        var responseA = await clientA.GetFromJsonAsync<List<ServiceResponse>>($"/v1/tenants/{tenantA}/services");
        var responseB = await clientB.GetFromJsonAsync<List<ServiceResponse>>($"/v1/tenants/{tenantB}/services");

        Assert.Single(responseA!);
        Assert.Equal(serviceA.Id, responseA![0].Id);
        Assert.DoesNotContain(responseB!, s => s.Id == serviceA.Id);
    }

    [Fact]
    public async Task GetMetrics_TenantB_CannotAccessTenantAService()
    {
        var (_, _, serviceA) = await SeedTenantWithServiceAsync("service-a-metrics");
        var (clientB, tenantB, _) = await SeedTenantWithServiceAsync("service-b-metrics");

        // Tenant B requesting a service id that does not belong to its own
        // tenant (even under its own tenantId) must resolve to "not found".
        var response = await clientB.GetAsync($"/v1/tenants/{tenantB}/services/{serviceA.Id}/metrics?range=1h");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMetrics_TenantA_CanAccessOwnService()
    {
        var (clientA, tenantA, serviceA) = await SeedTenantWithServiceAsync("service-a-own-metrics");

        var response = await clientA.GetAsync($"/v1/tenants/{tenantA}/services/{serviceA.Id}/metrics?range=1h");
        response.EnsureSuccessStatusCode();
        var rollups = await response.Content.ReadFromJsonAsync<List<MetricRollupResponse>>();

        Assert.Single(rollups!);
    }
}
