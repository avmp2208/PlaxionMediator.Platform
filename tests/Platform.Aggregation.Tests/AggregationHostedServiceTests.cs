using Microsoft.Extensions.Logging.Abstractions;
using Platform.Storage;
using Xunit;

namespace Platform.Aggregation.Tests;

public class AggregationHostedServiceTests
{
    [Fact]
    public async Task RunOnceAsync_PersistsRollupsAndRegistersService()
    {
        var sampleStore = new InMemoryMetricSampleStore();
        var rollupStore = new InMemoryAggregateRollupStore();
        var serviceCatalog = new InMemoryServiceCatalog();
        var tenantId = Guid.NewGuid();

        await sampleStore.AddAsync(new MetricSample
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ServiceName = "checkout-api",
            Environment = "prod",
            MetricName = "http.request.duration",
            Value = 42,
            Timestamp = DateTimeOffset.UtcNow
        });

        var hostedService = new AggregationHostedService(
            sampleStore, rollupStore, serviceCatalog, NullLogger<AggregationHostedService>.Instance);

        await hostedService.RunOnceAsync(CancellationToken.None);

        var services = await serviceCatalog.ListByTenantAsync(tenantId);
        var service = Assert.Single(services);

        var rollups = await rollupStore.QueryAsync(
            tenantId, service.Id, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1));

        Assert.Single(rollups);
        Assert.Equal(42, rollups[0].Avg);
    }
}
