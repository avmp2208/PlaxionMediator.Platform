using Platform.Storage;
using Xunit;

namespace Platform.Storage.Tests;

public class InMemoryMetricSampleStoreTests
{
    [Fact]
    public async Task AddAsync_PersistsSample_RetrievableByTenant()
    {
        var store = new InMemoryMetricSampleStore();
        var tenantId = Guid.NewGuid();
        var sample = new MetricSample
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ServiceName = "svc-a",
            Environment = "prod",
            MetricName = "cpu.usage",
            Value = 42.0,
            Timestamp = DateTimeOffset.UtcNow
        };

        await store.AddAsync(sample);
        var results = await store.GetByTenantAsync(tenantId);

        Assert.Single(results);
        Assert.Equal(sample.MetricName, results[0].MetricName);
    }

    [Fact]
    public async Task GetByTenantAsync_CrossTenantIsolation_TenantBHasZeroAccessToTenantAData()
    {
        var store = new InMemoryMetricSampleStore();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await store.AddAsync(new MetricSample
        {
            Id = Guid.NewGuid(),
            TenantId = tenantA,
            ServiceName = "svc-a",
            Environment = "prod",
            MetricName = "cpu.usage",
            Value = 10,
            Timestamp = DateTimeOffset.UtcNow
        });

        var tenantBResults = await store.GetByTenantAsync(tenantB);

        Assert.Empty(tenantBResults);
    }

    [Fact]
    public async Task AddAsync_RespectsCancellation()
    {
        var store = new InMemoryMetricSampleStore();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sample = new MetricSample
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ServiceName = "svc-a",
            Environment = "prod",
            MetricName = "cpu.usage",
            Value = 1,
            Timestamp = DateTimeOffset.UtcNow
        };

        await Assert.ThrowsAsync<OperationCanceledException>(() => store.AddAsync(sample, cts.Token));
    }
}
