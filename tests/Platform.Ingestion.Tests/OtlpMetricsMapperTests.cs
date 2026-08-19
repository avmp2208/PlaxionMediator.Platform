using System.Linq;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Resource.V1;
using Platform.Ingestion;
using Xunit;

namespace Platform.Ingestion.Tests;

public class OtlpMetricsMapperTests
{
    private static ExportMetricsServiceRequest BuildRequest(
        string metricName,
        double value,
        string? requestType,
        AggregationTemporality temporality = AggregationTemporality.Unspecified,
        ulong? timeUnixNano = null)
    {
        var nowNano = timeUnixNano ?? (ulong)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L);
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
            Sum = new Sum { AggregationTemporality = temporality, IsMonotonic = true }
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

    /// <summary>Adds a second Sum data point (for another request type) to the same metric on an
    /// existing request, so a single export can carry multiple distinct request types.</summary>
    private static void AddSumDataPoint(ExportMetricsServiceRequest request, string metricName, double value, string? requestType, ulong? timeUnixNano = null)
    {
        var nowNano = timeUnixNano ?? (ulong)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L);
        var metric = request.ResourceMetrics[0].ScopeMetrics[0].Metrics.Single(m => m.Name == metricName);

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
    }

    private static ExportMetricsServiceRequest BuildHistogramRequest(
        string metricName,
        ulong count,
        double sum,
        string? requestType,
        AggregationTemporality temporality,
        ulong? timeUnixNano = null)
    {
        var nowNano = timeUnixNano ?? (ulong)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L);
        var request = new ExportMetricsServiceRequest();
        var resourceMetrics = new ResourceMetrics { Resource = new Resource() };
        resourceMetrics.Resource.Attributes.Add(new KeyValue
        {
            Key = "service.name",
            Value = new AnyValue { StringValue = "plaxion-mediator-app" }
        });

        var scopeMetrics = new ScopeMetrics();
        var metric = new Metric
        {
            Name = metricName,
            Histogram = new Histogram { AggregationTemporality = temporality }
        };

        var dataPoint = new HistogramDataPoint
        {
            Count = count,
            Sum = sum,
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

        metric.Histogram.DataPoints.Add(dataPoint);
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

    // --- Cumulative OTel counter semantics (idle-period correctness) -------------------------

    [Fact]
    public void Map_CumulativeSum_FirstObservation_IsTreatedAsBaselineDelta()
    {
        var tenantId = Guid.NewGuid();
        var state = new CumulativeMetricStateStore();
        var request = BuildRequest("plaxionmediator.request.count", 10, "CreateOrderRequest", AggregationTemporality.Cumulative);

        var result = OtlpMetricsMapper.Map(request, tenantId, state);

        var sample = Assert.Single(result);
        Assert.Equal(10, sample.Value);
    }

    [Fact]
    public void Map_CumulativeSum_RepeatedIdenticalValue_ProducesNoSample()
    {
        // Simulates an idle instrumented app: the OTel SDK keeps periodically exporting the same
        // running-total counter value even though no new requests executed. This MUST NOT be
        // interpreted as new executions.
        var tenantId = Guid.NewGuid();
        var state = new CumulativeMetricStateStore();

        var first = BuildRequest("plaxionmediator.request.count", 10, "CreateOrderRequest", AggregationTemporality.Cumulative);
        var idleExport = BuildRequest("plaxionmediator.request.count", 10, "CreateOrderRequest", AggregationTemporality.Cumulative);

        var firstResult = OtlpMetricsMapper.Map(first, tenantId, state);
        Assert.Single(firstResult);

        var idleResult = OtlpMetricsMapper.Map(idleExport, tenantId, state);
        Assert.Empty(idleResult);
    }

    [Fact]
    public void Map_CumulativeSum_IncreasedValue_ProducesDeltaOnly()
    {
        // 12:00 total = 10, 12:01 total = 25 -> 15 new executions in the second interval, not 25.
        var tenantId = Guid.NewGuid();
        var state = new CumulativeMetricStateStore();

        var first = BuildRequest("plaxionmediator.request.count", 10, "CreateOrderRequest", AggregationTemporality.Cumulative);
        var second = BuildRequest("plaxionmediator.request.count", 25, "CreateOrderRequest", AggregationTemporality.Cumulative);

        OtlpMetricsMapper.Map(first, tenantId, state);
        var secondResult = OtlpMetricsMapper.Map(second, tenantId, state);

        var sample = Assert.Single(secondResult);
        Assert.Equal(15, sample.Value);
    }

    [Fact]
    public void Map_CumulativeSum_TrafficResumesAfterIdlePeriod_ProducesCorrectDelta()
    {
        // 10 -> (idle: 10, 10) -> 18: traffic resumes, delta must be 8, not 18.
        var tenantId = Guid.NewGuid();
        var state = new CumulativeMetricStateStore();

        OtlpMetricsMapper.Map(BuildRequest("plaxionmediator.request.count", 10, "CreateOrderRequest", AggregationTemporality.Cumulative), tenantId, state);
        Assert.Empty(OtlpMetricsMapper.Map(BuildRequest("plaxionmediator.request.count", 10, "CreateOrderRequest", AggregationTemporality.Cumulative), tenantId, state));
        Assert.Empty(OtlpMetricsMapper.Map(BuildRequest("plaxionmediator.request.count", 10, "CreateOrderRequest", AggregationTemporality.Cumulative), tenantId, state));

        var resumed = OtlpMetricsMapper.Map(BuildRequest("plaxionmediator.request.count", 18, "CreateOrderRequest", AggregationTemporality.Cumulative), tenantId, state);

        var sample = Assert.Single(resumed);
        Assert.Equal(8, sample.Value);
    }

    [Fact]
    public void Map_CumulativeSum_CounterReset_TreatsLowerValueAsFreshBaseline()
    {
        // e.g. the instrumented process restarted, so its in-memory cumulative counter reset to a
        // small number instead of continuing from the old total.
        var tenantId = Guid.NewGuid();
        var state = new CumulativeMetricStateStore();

        OtlpMetricsMapper.Map(BuildRequest("plaxionmediator.request.count", 100, "CreateOrderRequest", AggregationTemporality.Cumulative), tenantId, state);
        var afterReset = OtlpMetricsMapper.Map(BuildRequest("plaxionmediator.request.count", 3, "CreateOrderRequest", AggregationTemporality.Cumulative), tenantId, state);

        var sample = Assert.Single(afterReset);
        Assert.Equal(3, sample.Value);
    }

    [Fact]
    public void Map_CumulativeSum_AllDataPointsIdle_ReturnsEmptyWithoutThrowing()
    {
        // A valid OTLP export that legitimately carries zero new activity must be accepted (200/202),
        // not rejected as an empty/malformed payload.
        var tenantId = Guid.NewGuid();
        var state = new CumulativeMetricStateStore();

        OtlpMetricsMapper.Map(BuildRequest("plaxionmediator.request.count", 5, "CreateOrderRequest", AggregationTemporality.Cumulative), tenantId, state);
        var idle = OtlpMetricsMapper.Map(BuildRequest("plaxionmediator.request.count", 5, "CreateOrderRequest", AggregationTemporality.Cumulative), tenantId, state);

        Assert.Empty(idle);
    }

    [Fact]
    public void Map_NonCumulativeSum_RepeatedZeroValue_IsPassedThroughUnchanged()
    {
        // Non-cumulative (unspecified) temporality preserves pre-existing pass-through behavior -
        // no delta tracking is applied, so existing behavior/tests are unaffected.
        var tenantId = Guid.NewGuid();
        var request = BuildRequest("plaxionmediator.request.count", 1, "CreateOrderRequest");

        var result = OtlpMetricsMapper.Map(request, tenantId);

        var sample = Assert.Single(result);
        Assert.Equal(1, sample.Value);
    }

    [Fact]
    public void Map_CumulativeHistogram_RepeatedIdenticalCountAndSum_ProducesNoSample()
    {
        // A cumulative duration histogram with no new observations (idle) must not fabricate a
        // "latency" sample from the unchanged running totals.
        var tenantId = Guid.NewGuid();
        var state = new CumulativeMetricStateStore();

        var first = BuildHistogramRequest("plaxionmediator.request.duration", count: 10, sum: 500, "CreateOrderRequest", AggregationTemporality.Cumulative);
        var idle = BuildHistogramRequest("plaxionmediator.request.duration", count: 10, sum: 500, "CreateOrderRequest", AggregationTemporality.Cumulative);

        Assert.Single(OtlpMetricsMapper.Map(first, tenantId, state));
        Assert.Empty(OtlpMetricsMapper.Map(idle, tenantId, state));
    }

    [Fact]
    public void Map_CumulativeHistogram_NewObservations_ProducesDeltaAverage()
    {
        var tenantId = Guid.NewGuid();
        var state = new CumulativeMetricStateStore();

        var first = BuildHistogramRequest("plaxionmediator.request.duration", count: 10, sum: 500, "CreateOrderRequest", AggregationTemporality.Cumulative);
        // +5 observations totalling +100ms in this interval -> average delta of 20ms.
        var second = BuildHistogramRequest("plaxionmediator.request.duration", count: 15, sum: 600, "CreateOrderRequest", AggregationTemporality.Cumulative);

        OtlpMetricsMapper.Map(first, tenantId, state);
        var secondResult = OtlpMetricsMapper.Map(second, tenantId, state);

        var sample = Assert.Single(secondResult);
        Assert.Equal(20, sample.Value);
    }

    [Fact]
    public void Map_CumulativeSum_RepeatedExecutionsOfSameRequestType_EachExportProducesANewDelta()
    {
        // Repeatedly calling the SAME endpoint/request type must show up as new activity on every
        // export where the cumulative counter actually moved - it must not be treated as "already
        // seen this request type" and suppressed. Three consecutive real increments (+3, +3, +3).
        var tenantId = Guid.NewGuid();
        var state = new CumulativeMetricStateStore();

        OtlpMetricsMapper.Map(BuildRequest("plaxionmediator.request.count", 3, "CreateOrderRequest", AggregationTemporality.Cumulative), tenantId, state);
        var second = OtlpMetricsMapper.Map(BuildRequest("plaxionmediator.request.count", 6, "CreateOrderRequest", AggregationTemporality.Cumulative), tenantId, state);
        var third = OtlpMetricsMapper.Map(BuildRequest("plaxionmediator.request.count", 9, "CreateOrderRequest", AggregationTemporality.Cumulative), tenantId, state);

        Assert.Equal(3, Assert.Single(second).Value);
        Assert.Equal(3, Assert.Single(third).Value);
    }

    [Fact]
    public void Map_CumulativeSum_NewRequestTypeBehavesTheSameAsExistingRequestType()
    {
        // A brand-new request type must not be treated differently from one that has already been
        // seen: both get their own independent baseline and both produce a sample once they have a
        // prior observation to diff against. Here TypeA has an existing baseline (idle on its second
        // export), while TypeB is seen here for the very first time.
        var tenantId = Guid.NewGuid();
        var state = new CumulativeMetricStateStore();

        OtlpMetricsMapper.Map(BuildRequest("plaxionmediator.request.count", 10, "TypeA", AggregationTemporality.Cumulative), tenantId, state);

        var combinedExport = BuildRequest("plaxionmediator.request.count", 10, "TypeA", AggregationTemporality.Cumulative);
        AddSumDataPoint(combinedExport, "plaxionmediator.request.count", 5, "TypeB");

        var result = OtlpMetricsMapper.Map(combinedExport, tenantId, state);

        // TypeA is idle (10 -> 10, no delta); TypeB is a first-ever observation, so its raw value
        // becomes its own baseline delta - exactly like TypeA's very first export did above.
        var sample = Assert.Single(result);
        Assert.Equal("TypeB", sample.RequestType);
        Assert.Equal(5, sample.Value);
    }
}
