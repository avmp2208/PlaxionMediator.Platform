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

    private async Task<(HttpClient Client, Guid TenantId, Service Service, string ApiKey)> SeedTenantWithServiceAsync(string serviceName)
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
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, created.PlaintextKey);
        return (client, tenantId, service, created.PlaintextKey);
    }

    [Fact]
    public async Task GetServices_NoApiKey_Returns401()
    {
        var (_, tenantA, _, _) = await SeedTenantWithServiceAsync("service-no-key");
        var anonymousClient = _factory.CreateClient();

        var response = await anonymousClient.GetAsync($"/v1/tenants/{tenantA}/services");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetServices_OnlyReturnsOwnTenantServices()
    {
        var (clientA, tenantA, serviceA, _) = await SeedTenantWithServiceAsync("service-a");
        var (clientB, tenantB, _, _) = await SeedTenantWithServiceAsync("service-b");

        var responseA = await clientA.GetFromJsonAsync<List<ServiceResponse>>($"/v1/tenants/{tenantA}/services");
        var responseB = await clientB.GetFromJsonAsync<List<ServiceResponse>>($"/v1/tenants/{tenantB}/services");

        Assert.Single(responseA!);
        Assert.Equal(serviceA.Id, responseA![0].Id);
        Assert.DoesNotContain(responseB!, s => s.Id == serviceA.Id);
    }

    [Fact]
    public async Task GetServices_TenantA_KeyCannotReadTenantB_ServicesEvenWithTenantBIdInUrl()
    {
        var (clientA, _, _, _) = await SeedTenantWithServiceAsync("service-a-cross");
        var (_, tenantB, _, _) = await SeedTenantWithServiceAsync("service-b-cross");

        // Tenant A's API key used against Tenant B's real TenantId in the
        // URL must resolve to "not found", never to Tenant B's data.
        var response = await clientA.GetAsync($"/v1/tenants/{tenantB}/services");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMetrics_TenantB_CannotAccessTenantAService()
    {
        var (_, _, serviceA, _) = await SeedTenantWithServiceAsync("service-a-metrics");
        var (clientB, tenantB, _, _) = await SeedTenantWithServiceAsync("service-b-metrics");

        // Tenant B requesting a service id that does not belong to its own
        // tenant (even under its own tenantId) must resolve to "not found".
        var response = await clientB.GetAsync($"/v1/tenants/{tenantB}/services/{serviceA.Id}/metrics?range=1h");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMetrics_TenantA_KeyCannotReadTenantB_MetricsEvenWithTenantBIdInUrl()
    {
        var (clientA, _, serviceAOwn, _) = await SeedTenantWithServiceAsync("service-a-metrics-cross");
        var (_, tenantB, serviceB, _) = await SeedTenantWithServiceAsync("service-b-metrics-cross");

        // Tenant A's API key against Tenant B's real TenantId + service id
        // must resolve to "not found", never leaking Tenant B's metrics.
        var response = await clientA.GetAsync($"/v1/tenants/{tenantB}/services/{serviceB.Id}/metrics?range=1h");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        _ = serviceAOwn;
    }

    [Fact]
    public async Task GetMetrics_TenantA_CanAccessOwnService()
    {
        var (clientA, tenantA, serviceA, _) = await SeedTenantWithServiceAsync("service-a-own-metrics");

        var response = await clientA.GetAsync($"/v1/tenants/{tenantA}/services/{serviceA.Id}/metrics?range=1h");
        response.EnsureSuccessStatusCode();
        var rollups = await response.Content.ReadFromJsonAsync<List<MetricRollupResponse>>();

        Assert.Single(rollups!);
    }

    [Fact]
    public async Task GetMetrics_ResponseIncludesRequestTypeField()
    {
        var (clientA, tenantA, serviceA, _) = await SeedTenantWithServiceAsync("service-a-request-type");

        using (var scope = _factory.Services.CreateScope())
        {
            var rollupStore = scope.ServiceProvider.GetRequiredService<IAggregateRollupStore>();
            var now = DateTimeOffset.UtcNow;
            await rollupStore.AddRangeAsync(new[]
            {
                new AggregateRollup
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantA,
                    ServiceName = serviceA.Name,
                    Environment = "prod",
                    MetricName = "plaxionmediator.request.count",
                    RequestType = "CreateOrderRequest",
                    BucketStart = now.AddMinutes(-1),
                    BucketEnd = now,
                    Count = 3,
                    Min = 1,
                    Max = 1,
                    Avg = 1,
                    P50 = 1,
                    P95 = 1,
                    P99 = 1
                }
            });
        }

        var response = await clientA.GetAsync($"/v1/tenants/{tenantA}/services/{serviceA.Id}/metrics?range=1h");
        response.EnsureSuccessStatusCode();
        var rollups = await response.Content.ReadFromJsonAsync<List<MetricRollupResponse>>();

        Assert.Contains(rollups!, r => r.MetricName == "plaxionmediator.request.count" && r.RequestType == "CreateOrderRequest");
    }

    [Fact]
    public async Task GetRequestTypeRanking_NoApiKey_Returns401()
    {
        var (_, tenantA, serviceA, _) = await SeedTenantWithServiceAsync("service-ranking-no-key");
        var anonymousClient = _factory.CreateClient();

        var response = await anonymousClient.GetAsync($"/v1/tenants/{tenantA}/services/{serviceA.Id}/request-types?range=1h");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetRequestTypeRanking_TenantA_KeyCannotReadTenantB_EvenWithTenantBIdInUrl()
    {
        var (clientA, _, serviceAOwn, _) = await SeedTenantWithServiceAsync("service-a-ranking-cross");
        var (_, tenantB, serviceB, _) = await SeedTenantWithServiceAsync("service-b-ranking-cross");

        // Tenant A's API key against Tenant B's real TenantId + service id
        // must resolve to "not found", never leaking Tenant B's ranking data.
        var response = await clientA.GetAsync($"/v1/tenants/{tenantB}/services/{serviceB.Id}/request-types?range=1h");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        _ = serviceAOwn;
    }

    [Fact]
    public async Task GetRequestTypeRanking_TenantB_CannotAccessTenantAService()
    {
        var (_, _, serviceA, _) = await SeedTenantWithServiceAsync("service-a-ranking");
        var (clientB, tenantB, _, _) = await SeedTenantWithServiceAsync("service-b-ranking");

        var response = await clientB.GetAsync($"/v1/tenants/{tenantB}/services/{serviceA.Id}/request-types?range=1h");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
