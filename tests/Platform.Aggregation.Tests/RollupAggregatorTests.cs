using Platform.Aggregation;
using Platform.Storage;
using Xunit;

namespace Platform.Aggregation.Tests;

public class RollupAggregatorTests
{
    [Fact]
    public void Compute_KnownValues_ProducesExpectedStatistics()
    {
        var tenantId = Guid.NewGuid();
        var bucketStart = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var values = new double[] { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 };

        var samples = values.Select((v, i) => new MetricSample
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ServiceName = "checkout-api",
            Environment = "prod",
            MetricName = "http.request.duration",
            Value = v,
            Timestamp = bucketStart.AddSeconds(i)
        }).ToList();

        var rollups = RollupAggregator.Compute(samples, TimeSpan.FromMinutes(1));

        var rollup = Assert.Single(rollups);
        Assert.Equal(10, rollup.Count);
        Assert.Equal(10, rollup.Min);
        Assert.Equal(100, rollup.Max);
        Assert.Equal(55, rollup.Avg);
        Assert.Equal(50, rollup.P50);
        Assert.Equal(100, rollup.P95);
        Assert.Equal(100, rollup.P99);
        Assert.Equal(tenantId, rollup.TenantId);
    }

    [Fact]
    public void Compute_DifferentBuckets_ProducesSeparateRollups()
    {
        var tenantId = Guid.NewGuid();
        var samples = new List<MetricSample>
        {
            new()
            {
                TenantId = tenantId, ServiceName = "svc", Environment = "prod",
                MetricName = "m", Value = 1, Timestamp = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)
            },
            new()
            {
                TenantId = tenantId, ServiceName = "svc", Environment = "prod",
                MetricName = "m", Value = 2, Timestamp = new DateTimeOffset(2024, 1, 1, 0, 5, 0, TimeSpan.Zero)
            }
        };

        var rollups = RollupAggregator.Compute(samples, TimeSpan.FromMinutes(1));

        Assert.Equal(2, rollups.Count);
    }

    [Fact]
    public void Compute_DifferentTenants_NeverMixedIntoSameRollup()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var samples = new List<MetricSample>
        {
            new() { TenantId = tenantA, ServiceName = "svc", Environment = "prod", MetricName = "m", Value = 10, Timestamp = timestamp },
            new() { TenantId = tenantB, ServiceName = "svc", Environment = "prod", MetricName = "m", Value = 999, Timestamp = timestamp }
        };

        var rollups = RollupAggregator.Compute(samples, TimeSpan.FromMinutes(1));

        Assert.Equal(2, rollups.Count);
        Assert.All(rollups, r => Assert.Equal(1, r.Count));
    }

    [Fact]
    public void Compute_DifferentRequestTypes_ProducesSeparateRollups()
    {
        var tenantId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var samples = new List<MetricSample>
        {
            new()
            {
                TenantId = tenantId, ServiceName = "svc", Environment = "prod",
                MetricName = "plaxionmediator.request.count", Value = 1, Timestamp = timestamp, RequestType = "CreateOrderRequest"
            },
            new()
            {
                TenantId = tenantId, ServiceName = "svc", Environment = "prod",
                MetricName = "plaxionmediator.request.count", Value = 1, Timestamp = timestamp, RequestType = "GetOrderRequest"
            }
        };

        var rollups = RollupAggregator.Compute(samples, TimeSpan.FromMinutes(1));

        Assert.Equal(2, rollups.Count);
        Assert.Contains(rollups, r => r.RequestType == "CreateOrderRequest" && r.Count == 1);
        Assert.Contains(rollups, r => r.RequestType == "GetOrderRequest" && r.Count == 1);
    }

    [Fact]
    public void Compute_NullRequestType_StillMergesIntoSingleRollup()
    {
        var tenantId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var samples = new List<MetricSample>
        {
            new() { TenantId = tenantId, ServiceName = "svc", Environment = "prod", MetricName = "m", Value = 1, Timestamp = timestamp, RequestType = null },
            new() { TenantId = tenantId, ServiceName = "svc", Environment = "prod", MetricName = "m", Value = 2, Timestamp = timestamp, RequestType = null }
        };

        var rollups = RollupAggregator.Compute(samples, TimeSpan.FromMinutes(1));

        var rollup = Assert.Single(rollups);
        Assert.Null(rollup.RequestType);
        Assert.Equal(2, rollup.Count);
    }
}
