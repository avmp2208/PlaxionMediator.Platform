using Platform.Storage;
using Xunit;

namespace Platform.Storage.Tests;

public class AggregateRollupStoreTests
{
    [Fact]
    public async Task Query_CrossTenant_ReturnsNoRollups()
    {
        var store = new InMemoryAggregateRollupStore();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var serviceId = ServiceIdGenerator.Compute(tenantA, "checkout-api", "prod");
        var now = DateTimeOffset.UtcNow;

        await store.AddRangeAsync(new[]
        {
            new AggregateRollup
            {
                Id = Guid.NewGuid(),
                TenantId = tenantA,
                ServiceName = "checkout-api",
                Environment = "prod",
                MetricName = "m",
                BucketStart = now,
                BucketEnd = now.AddMinutes(1),
                Count = 1,
                Min = 1,
                Max = 1,
                Avg = 1,
                P50 = 1,
                P95 = 1,
                P99 = 1
            }
        });

        var resultForOwner = await store.QueryAsync(tenantA, serviceId, now.AddHours(-1), now.AddHours(1));
        var resultForOtherTenant = await store.QueryAsync(tenantB, serviceId, now.AddHours(-1), now.AddHours(1));

        Assert.Single(resultForOwner);
        Assert.Empty(resultForOtherTenant);
    }

    [Fact]
    public async Task AddRangeAsync_SameBucketRecomputed_UpsertsInsteadOfDuplicating()
    {
        var store = new InMemoryAggregateRollupStore();
        var tenantId = Guid.NewGuid();
        var serviceId = ServiceIdGenerator.Compute(tenantId, "checkout-api", "prod");
        var now = DateTimeOffset.UtcNow;

        var firstRun = new AggregateRollup
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ServiceName = "checkout-api",
            Environment = "prod",
            MetricName = "m",
            BucketStart = now,
            BucketEnd = now.AddMinutes(1),
            Count = 1,
            Min = 1,
            Max = 1,
            Avg = 1,
            P50 = 1,
            P95 = 1,
            P99 = 1
        };
        await store.AddRangeAsync(new[] { firstRun });

        // Simulates a subsequent aggregation job run recomputing the exact
        // same bucket (same tenant/service/metric/request type/bucket start)
        // after another sample arrived - the count should be updated in
        // place, not appended as a second row.
        var secondRun = new AggregateRollup
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ServiceName = "checkout-api",
            Environment = "prod",
            MetricName = "m",
            BucketStart = now,
            BucketEnd = now.AddMinutes(1),
            Count = 2,
            Min = 1,
            Max = 2,
            Avg = 1.5,
            P50 = 1,
            P95 = 2,
            P99 = 2
        };
        await store.AddRangeAsync(new[] { secondRun });

        var result = await store.QueryAsync(tenantId, serviceId, now.AddHours(-1), now.AddHours(1));

        Assert.Single(result);
        Assert.Equal(2, result[0].Count);
        Assert.Equal(2, result[0].Max);
    }
}
