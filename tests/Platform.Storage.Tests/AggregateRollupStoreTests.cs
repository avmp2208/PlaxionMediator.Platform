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
}
