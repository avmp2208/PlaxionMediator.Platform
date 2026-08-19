using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Platform.Storage;
using Xunit;

namespace Platform.Aggregation.Tests;

public class RetentionHostedServiceTests
{
    [Fact]
    public async Task RunOnceAsync_EvictsSamplesOlderThanConfiguredRetention_KeepsFreshOnes()
    {
        var sampleStore = new InMemoryMetricSampleStore();
        var tenantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await sampleStore.AddAsync(new MetricSample
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ServiceName = "svc",
            Environment = "prod",
            MetricName = "cpu.usage",
            Value = 1,
            Timestamp = now - TimeSpan.FromDays(10)
        });
        await sampleStore.AddAsync(new MetricSample
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ServiceName = "svc",
            Environment = "prod",
            MetricName = "cpu.usage",
            Value = 2,
            Timestamp = now
        });

        var options = Options.Create(new RetentionOptions { RawSampleRetention = TimeSpan.FromDays(7) });
        var hostedService = new RetentionHostedService(sampleStore, options, NullLogger<RetentionHostedService>.Instance);

        await hostedService.RunOnceAsync(CancellationToken.None);

        var remaining = await sampleStore.GetByTenantAsync(tenantId);
        var single = Assert.Single(remaining);
        Assert.Equal(2, single.Value);
    }

    [Fact]
    public async Task RunOnceAsync_PreservesTenantIsolation()
    {
        var sampleStore = new InMemoryMetricSampleStore();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var oldTimestamp = now - TimeSpan.FromDays(30);

        await sampleStore.AddAsync(new MetricSample
        {
            Id = Guid.NewGuid(),
            TenantId = tenantA,
            ServiceName = "svc-a",
            Environment = "prod",
            MetricName = "cpu.usage",
            Value = 1,
            Timestamp = oldTimestamp
        });
        await sampleStore.AddAsync(new MetricSample
        {
            Id = Guid.NewGuid(),
            TenantId = tenantB,
            ServiceName = "svc-b",
            Environment = "prod",
            MetricName = "cpu.usage",
            Value = 2,
            Timestamp = now
        });

        var options = Options.Create(new RetentionOptions { RawSampleRetention = TimeSpan.FromDays(7) });
        var hostedService = new RetentionHostedService(sampleStore, options, NullLogger<RetentionHostedService>.Instance);

        await hostedService.RunOnceAsync(CancellationToken.None);

        Assert.Empty(await sampleStore.GetByTenantAsync(tenantA));
        Assert.Single(await sampleStore.GetByTenantAsync(tenantB));
    }

    [Fact]
    public async Task RunOnceAsync_DoesNotAffectAggregateRollups()
    {
        var sampleStore = new InMemoryMetricSampleStore();
        var rollupStore = new InMemoryAggregateRollupStore();
        var tenantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var oldTimestamp = now - TimeSpan.FromDays(30);

        await sampleStore.AddAsync(new MetricSample
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ServiceName = "checkout",
            Environment = "prod",
            MetricName = "latency",
            Value = 1,
            Timestamp = oldTimestamp
        });

        var serviceId = ServiceIdGenerator.Compute(tenantId, "checkout", "prod");
        await rollupStore.AddRangeAsync(new[]
        {
            new AggregateRollup
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ServiceName = "checkout",
                Environment = "prod",
                MetricName = "latency",
                BucketStart = oldTimestamp,
                BucketEnd = oldTimestamp.AddMinutes(1),
                Count = 1,
                Min = 1,
                Max = 1,
                Avg = 1,
                P50 = 1,
                P95 = 1,
                P99 = 1
            }
        });

        var options = Options.Create(new RetentionOptions { RawSampleRetention = TimeSpan.FromDays(7) });
        var hostedService = new RetentionHostedService(sampleStore, options, NullLogger<RetentionHostedService>.Instance);

        await hostedService.RunOnceAsync(CancellationToken.None);

        Assert.Empty(await sampleStore.GetByTenantAsync(tenantId));
        Assert.Single(await rollupStore.QueryAsync(
            tenantId, serviceId, oldTimestamp.AddMinutes(-1), oldTimestamp.AddMinutes(2)));
    }
}
