using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Resource.V1;
using Platform.Ingestion;
using Xunit;

namespace Platform.Ingestion.Tests;

public class OtlpMetricsMapperTests
{
    private static ExportMetricsServiceRequest BuildRequest(string metricName, double value, string? requestType)
    {
        var nowNano = (ulong)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L);
        var request = new ExportMetricsServiceRequest();
        var resourceMetrics = new ResourceMetrics
        {
            Resource = new Resource()
        };
        resourceMetrics.Resource.Attributes.Add(new KeyValue
        {
            Key = "service.name",
            Value = new AnyValue { StringValue = "plaxion-mediator-app" }
        });

        var scopeMetrics = new ScopeMetrics();
        var metric = new Metric
        {
            Name = metricName,
            Sum = new Sum()
        };

        var dataPoint = new NumberDataPoint
        {
            AsDouble = value,
            TimeUnixNano = nowNano
        };

        if (requestType is not null)
        {
            dataPoint.Attributes.Add(new KeyValue
            {
                Key = "plaxionmediator.request.type",
                Value = new AnyValue { StringValue = requestType }
            });
        }

        metric.Sum.DataPoints.Add(dataPoint);
        scopeMetrics.Metrics.Add(metric);
        resourceMetrics.ScopeMetrics.Add(scopeMetrics);
        request.ResourceMetrics.Add(resourceMetrics);
        return request;
    }

    [Fact]
    public void Map_DataPointWithRequestTypeAttribute_PopulatesRequestType()
    {
        var request = BuildRequest("plaxionmediator.request.count", 1, "CreateOrderRequest");

        var result = OtlpMetricsMapper.Map(request, Guid.NewGuid());

        var sample = Assert.Single(result);
        Assert.Equal("CreateOrderRequest", sample.RequestType);
    }

    [Fact]
    public void Map_DataPointWithoutRequestTypeAttribute_LeavesRequestTypeNull()
    {
        var request = BuildRequest("demo.gauge", 1, requestType: null);

        var result = OtlpMetricsMapper.Map(request, Guid.NewGuid());

        var sample = Assert.Single(result);
        Assert.Null(sample.RequestType);
    }

    [Fact]
    public void Map_HighCardinalityRequestType_IsRejected()
    {
        var request = BuildRequest("plaxionmediator.request.count", 1, new string('x', 500));

        Assert.Throws<ArgumentException>(() => OtlpMetricsMapper.Map(request, Guid.NewGuid()));
    }

    [Fact]
    public void Map_TooManyDistinctRequestTypes_IsRejected()
    {
        var nowNano = (ulong)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L);
        var request = new ExportMetricsServiceRequest();
        var resourceMetrics = new ResourceMetrics { Resource = new Resource() };
        resourceMetrics.Resource.Attributes.Add(new KeyValue
        {
            Key = "service.name",
            Value = new AnyValue { StringValue = "plaxion-mediator-app" }
        });

        var scopeMetrics = new ScopeMetrics();
        var metric = new Metric { Name = "plaxionmediator.request.count", Sum = new Sum() };
        for (var i = 0; i < 101; i++)
        {
            var dataPoint = new NumberDataPoint { AsDouble = 1, TimeUnixNano = nowNano };
            dataPoint.Attributes.Add(new KeyValue
            {
                Key = "plaxionmediator.request.type",
                Value = new AnyValue { StringValue = $"RequestType{i}" }
            });
            metric.Sum.DataPoints.Add(dataPoint);
        }

        scopeMetrics.Metrics.Add(metric);
        resourceMetrics.ScopeMetrics.Add(scopeMetrics);
        request.ResourceMetrics.Add(resourceMetrics);

        var ex = Assert.Throws<ArgumentException>(() => OtlpMetricsMapper.Map(request, Guid.NewGuid()));
        Assert.Contains("distinct request types", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
