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

    [Fact]
    public async Task DeleteOlderThanAsync_RemovesExpiredSamples_KeepsNonExpired()
    {
        var store = new InMemoryMetricSampleStore();
        var tenantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var expired = new MetricSample
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ServiceName = "svc-a",
            Environment = "prod",
            MetricName = "cpu.usage",
            Value = 1,
            Timestamp = now - TimeSpan.FromDays(10)
        };
        var fresh = new MetricSample
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ServiceName = "svc-a",
            Environment = "prod",
            MetricName = "cpu.usage",
            Value = 2,
            Timestamp = now
        };

        await store.AddAsync(expired);
        await store.AddAsync(fresh);

        await store.DeleteOlderThanAsync(now - TimeSpan.FromDays(7));

        var remaining = await store.GetByTenantAsync(tenantId);
        Assert.Single(remaining);
        Assert.Equal(fresh.Id, remaining[0].Id);
    }

    [Fact]
    public async Task DeleteOlderThanAsync_PreservesTenantIsolation()
    {
        var store = new InMemoryMetricSampleStore();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var oldTimestamp = now - TimeSpan.FromDays(30);

        await store.AddAsync(new MetricSample
        {
            Id = Guid.NewGuid(),
            TenantId = tenantA,
            ServiceName = "svc-a",
            Environment = "prod",
            MetricName = "cpu.usage",
            Value = 1,
            Timestamp = oldTimestamp
        });
        await store.AddAsync(new MetricSample
        {
            Id = Guid.NewGuid(),
            TenantId = tenantB,
            ServiceName = "svc-b",
            Environment = "prod",
            MetricName = "cpu.usage",
            Value = 2,
            Timestamp = oldTimestamp
        });

        // Only tenant A's expired sample should be removed by a threshold
        // that would also expire tenant B's sample - proving the deletion
        // is never selectively scoped by tenant in a way that leaks across
        // boundaries is verified by asserting BOTH old rows are gone (server-
        // wide eviction), while a still-fresh tenant B row survives.
        await store.AddAsync(new MetricSample
        {
            Id = Guid.NewGuid(),
            TenantId = tenantB,
            ServiceName = "svc-b",
            Environment = "prod",
            MetricName = "cpu.usage",
            Value = 3,
            Timestamp = now
        });

        await store.DeleteOlderThanAsync(now - TimeSpan.FromDays(7));

        var tenantAResults = await store.GetByTenantAsync(tenantA);
        var tenantBResults = await store.GetByTenantAsync(tenantB);

        Assert.Empty(tenantAResults);
        Assert.Single(tenantBResults);
        Assert.Equal(3, tenantBResults[0].Value);
    }
}
