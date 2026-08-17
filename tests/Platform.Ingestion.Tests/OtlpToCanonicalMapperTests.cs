using Platform.Ingestion;
using Xunit;

namespace Platform.Ingestion.Tests;

public class OtlpToCanonicalMapperTests
{
    private static IngestRequest ValidRequest() => new()
    {
        Resource = new IngestResource { ServiceName = "checkout-api", Environment = "prod" },
        Metrics = new List<IngestMetricPoint>
        {
            new() { MetricName = "http.request.duration", Value = 12.5, TimeUnixNano = 1_700_000_000_000_000_000 }
        }
    };

    [Fact]
    public void Map_ValidRequest_IsAccepted()
    {
        var tenantId = Guid.NewGuid();
        var result = OtlpToCanonicalMapper.Map(ValidRequest(), tenantId);

        Assert.Single(result);
        Assert.Equal(tenantId, result[0].TenantId);
        Assert.Equal("checkout-api", result[0].ServiceName);
        Assert.Equal("prod", result[0].Environment);
        Assert.Equal("http.request.duration", result[0].MetricName);
    }

    [Fact]
    public void Map_MissingServiceName_IsRejected()
    {
        var request = ValidRequest();
        request.Resource.ServiceName = "";

        Assert.Throws<ArgumentException>(() => OtlpToCanonicalMapper.Map(request, Guid.NewGuid()));
    }

    [Fact]
    public void Map_NoMetrics_IsRejected()
    {
        var request = ValidRequest();
        request.Metrics.Clear();

        Assert.Throws<ArgumentException>(() => OtlpToCanonicalMapper.Map(request, Guid.NewGuid()));
    }

    [Fact]
    public void Map_MissingMetricName_IsRejected()
    {
        var request = ValidRequest();
        request.Metrics[0].MetricName = "";

        Assert.Throws<ArgumentException>(() => OtlpToCanonicalMapper.Map(request, Guid.NewGuid()));
    }

    [Fact]
    public void Map_HighCardinalityMetricName_IsRejected()
    {
        var request = ValidRequest();
        request.Metrics[0].MetricName = new string('x', 500);

        Assert.Throws<ArgumentException>(() => OtlpToCanonicalMapper.Map(request, Guid.NewGuid()));
    }

    [Fact]
    public void Map_TooManyDistinctMetricNames_IsRejected()
    {
        var request = ValidRequest();
        request.Metrics = Enumerable.Range(0, 201)
            .Select(i => new IngestMetricPoint
            {
                MetricName = $"metric.{i}",
                Value = 1,
                TimeUnixNano = 1_700_000_000_000_000_000
            })
            .ToList();

        var ex = Assert.Throws<ArgumentException>(() => OtlpToCanonicalMapper.Map(request, Guid.NewGuid()));
        Assert.Contains("distinct metric names", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Map_TooManySamples_IsRejected()
    {
        var request = ValidRequest();
        request.Metrics = Enumerable.Range(0, 5_001)
            .Select(_ => new IngestMetricPoint
            {
                MetricName = "same.metric",
                Value = 1,
                TimeUnixNano = 1_700_000_000_000_000_000
            })
            .ToList();

        var ex = Assert.Throws<ArgumentException>(() => OtlpToCanonicalMapper.Map(request, Guid.NewGuid()));
        Assert.Contains("metric points", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Map_NonFiniteValue_IsRejected()
    {
        var request = ValidRequest();
        request.Metrics[0].Value = double.NaN;

        Assert.Throws<ArgumentException>(() => OtlpToCanonicalMapper.Map(request, Guid.NewGuid()));
    }

    [Fact]
    public void Map_MissingEnvironment_DefaultsToUnknown()
    {
        var request = ValidRequest();
        request.Resource.Environment = "";

        var result = OtlpToCanonicalMapper.Map(request, Guid.NewGuid());

        Assert.Equal("unknown", result[0].Environment);
    }

    [Fact]
    public void Map_EveryTenantSample_HasTenantIdAttached()
    {
        var tenantId = Guid.NewGuid();
        var request = ValidRequest();
        request.Metrics.Add(new IngestMetricPoint { MetricName = "memory.used", Value = 1, TimeUnixNano = 1_700_000_000_000_000_000 });

        var result = OtlpToCanonicalMapper.Map(request, tenantId);

        Assert.All(result, s => Assert.Equal(tenantId, s.TenantId));
    }

    [Fact]
    public void Map_PointWithRequestType_PopulatesRequestType()
    {
        var request = ValidRequest();
        request.Metrics[0].RequestType = "CreateOrderRequest";

        var result = OtlpToCanonicalMapper.Map(request, Guid.NewGuid());

        Assert.Equal("CreateOrderRequest", result[0].RequestType);
    }

    [Fact]
    public void Map_PointWithoutRequestType_LeavesRequestTypeNull()
    {
        var result = OtlpToCanonicalMapper.Map(ValidRequest(), Guid.NewGuid());

        Assert.Null(result[0].RequestType);
    }

    [Fact]
    public void Map_HighCardinalityRequestType_IsRejected()
    {
        var request = ValidRequest();
        request.Metrics[0].RequestType = new string('x', 500);

        Assert.Throws<ArgumentException>(() => OtlpToCanonicalMapper.Map(request, Guid.NewGuid()));
    }

    [Fact]
    public void Map_TooManyDistinctRequestTypes_IsRejected()
    {
        var request = ValidRequest();
        request.Metrics = Enumerable.Range(0, 101)
            .Select(i => new IngestMetricPoint
            {
                MetricName = "plaxionmediator.request.count",
                Value = 1,
                TimeUnixNano = 1_700_000_000_000_000_000,
                RequestType = $"RequestType{i}"
            })
            .ToList();

        var ex = Assert.Throws<ArgumentException>(() => OtlpToCanonicalMapper.Map(request, Guid.NewGuid()));
        Assert.Contains("distinct request types", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
