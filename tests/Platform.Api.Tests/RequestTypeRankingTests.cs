using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Platform.Api;
using Platform.Identity;
using Platform.Storage;
using Xunit;

namespace Platform.Api.Tests;

/// <summary>
/// Tests for the server-side-paginated request-types ranking endpoint,
/// verifying it replicates the frontend's client-side buildRanking
/// aggregation (see metrics-view-model.ts).
/// </summary>
public class RequestTypeRankingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RequestTypeRankingTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, Guid TenantId, Service Service)> SeedTenantWithServiceAsync(string serviceName)
    {
        using var scope = _factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<ApiKeyService>();
        var serviceCatalog = scope.ServiceProvider.GetRequiredService<IServiceCatalog>();

        var tenantId = Guid.NewGuid();
        var created = await apiKeyService.CreateAsync(tenantId, "test-key");
        var service = await serviceCatalog.GetOrCreateAsync(tenantId, serviceName, "prod");

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, created.PlaintextKey);
        return (client, tenantId, service);
    }

    private async Task AddRequestTypeRollupsAsync(
        Guid tenantId,
        string serviceName,
        string requestType,
        int executionCount,
        double p95 = 0,
        double p99 = 0,
        int exceptionCount = 0)
    {
        using var scope = _factory.Services.CreateScope();
        var rollupStore = scope.ServiceProvider.GetRequiredService<IAggregateRollupStore>();
        var now = DateTimeOffset.UtcNow;

        var rollups = new List<AggregateRollup>
        {
            new()
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ServiceName = serviceName,
                Environment = "prod",
                MetricName = "plaxionmediator.request.count",
                RequestType = requestType,
                BucketStart = now.AddMinutes(-1),
                BucketEnd = now,
                Count = executionCount,
                Min = 1,
                Max = 1,
                Avg = 1,
                P50 = 1,
                P95 = 1,
                P99 = 1
            },
            new()
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ServiceName = serviceName,
                Environment = "prod",
                MetricName = "plaxionmediator.request.duration",
                RequestType = requestType,
                BucketStart = now.AddMinutes(-1),
                BucketEnd = now,
                Count = executionCount,
                Min = 1,
                Max = 1,
                Avg = 1,
                P50 = 1,
                P95 = p95,
                P99 = p99
            }
        };

        if (exceptionCount > 0)
        {
            rollups.Add(new AggregateRollup
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ServiceName = serviceName,
                Environment = "prod",
                MetricName = "plaxionmediator.pipeline_exception.count",
                RequestType = requestType,
                BucketStart = now.AddMinutes(-1),
                BucketEnd = now,
                Count = exceptionCount,
                Min = 1,
                Max = 1,
                Avg = 1,
                P50 = 1,
                P95 = 1,
                P99 = 1
            });
        }

        await rollupStore.AddRangeAsync(rollups);
    }

    [Fact]
    public async Task GetRequestTypeRanking_DefaultPagination_ReturnsPage1PageSize20()
    {
        var (client, tenantId, service) = await SeedTenantWithServiceAsync("ranking-default");
        await AddRequestTypeRollupsAsync(tenantId, service.Name, "TypeA", 10);

        var response = await client.GetFromJsonAsync<PagedRequestTypeRankingResponse>(
            $"/v1/tenants/{tenantId}/services/{service.Id}/request-types?range=1h");

        Assert.Equal(1, response!.Page);
        Assert.Equal(20, response.PageSize);
    }

    [Fact]
    public async Task GetRequestTypeRanking_FirstPage_OrdersByExecutionCountDescendingThenRequestTypeAscending()
    {
        var (client, tenantId, service) = await SeedTenantWithServiceAsync("ranking-order");

        // Two request types with tied execution counts should tie-break on requestType ascending.
        await AddRequestTypeRollupsAsync(tenantId, service.Name, "Zebra", 5);
        await AddRequestTypeRollupsAsync(tenantId, service.Name, "Alpha", 5);
        await AddRequestTypeRollupsAsync(tenantId, service.Name, "TopType", 100);

        var response = await client.GetFromJsonAsync<PagedRequestTypeRankingResponse>(
            $"/v1/tenants/{tenantId}/services/{service.Id}/request-types?range=1h&page=1&pageSize=20");

        Assert.Equal(3, response!.TotalItems);
        Assert.Equal(1, response.TotalPages);
        Assert.Equal(new[] { "TopType", "Alpha", "Zebra" }, response.Items.Select(i => i.RequestType));
    }

    [Fact]
    public async Task GetRequestTypeRanking_LaterPage_ReturnsCorrectSliceAndTotals()
    {
        var (client, tenantId, service) = await SeedTenantWithServiceAsync("ranking-paged");

        // 25 distinct request types, descending execution counts so ordering is deterministic
        // without relying on the requestType tie-break.
        for (var i = 0; i < 25; i++)
        {
            await AddRequestTypeRollupsAsync(tenantId, service.Name, $"Type{i:D2}", 100 - i);
        }

        var page1 = await client.GetFromJsonAsync<PagedRequestTypeRankingResponse>(
            $"/v1/tenants/{tenantId}/services/{service.Id}/request-types?range=1h&page=1&pageSize=20");
        var page2 = await client.GetFromJsonAsync<PagedRequestTypeRankingResponse>(
            $"/v1/tenants/{tenantId}/services/{service.Id}/request-types?range=1h&page=2&pageSize=20");

        Assert.Equal(25, page1!.TotalItems);
        Assert.Equal(2, page1.TotalPages);
        Assert.Equal(20, page1.Items.Count);
        Assert.Equal("Type00", page1.Items[0].RequestType);

        Assert.Equal(2, page2!.Page);
        Assert.Equal(5, page2.Items.Count);
        Assert.Equal("Type20", page2.Items[0].RequestType);
    }

    [Fact]
    public async Task GetRequestTypeRanking_NoMatchingExceptionRow_FailurePercentIsZero()
    {
        var (client, tenantId, service) = await SeedTenantWithServiceAsync("ranking-no-exceptions");
        await AddRequestTypeRollupsAsync(tenantId, service.Name, "CleanType", 42);

        var response = await client.GetFromJsonAsync<PagedRequestTypeRankingResponse>(
            $"/v1/tenants/{tenantId}/services/{service.Id}/request-types?range=1h");

        var item = Assert.Single(response!.Items);
        Assert.Equal("CleanType", item.RequestType);
        Assert.Equal(42, item.ExecutionCount);
        Assert.Equal(0, item.FailurePercent);
    }

    [Fact]
    public async Task GetRequestTypeRanking_WithExceptions_ComputesFailurePercent()
    {
        var (client, tenantId, service) = await SeedTenantWithServiceAsync("ranking-with-exceptions");
        await AddRequestTypeRollupsAsync(tenantId, service.Name, "FlakyType", 100, exceptionCount: 10);

        var response = await client.GetFromJsonAsync<PagedRequestTypeRankingResponse>(
            $"/v1/tenants/{tenantId}/services/{service.Id}/request-types?range=1h");

        var item = Assert.Single(response!.Items);
        Assert.Equal(10, item.FailurePercent);
    }
}
