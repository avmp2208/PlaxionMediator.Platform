using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Metrics.V1;
using Platform.Storage;

namespace Platform.Ingestion;

/// <summary>
/// Maps standard OTLP <see cref="ExportMetricsServiceRequest"/> payloads into
/// canonical <see cref="MetricSample"/> records. TenantId is supplied by the
/// authenticated API key context and is never taken from resource attributes.
/// </summary>
public static class OtlpMetricsMapper
{
    private const int MaxMetricNameLength = 256;
    private const int MaxServiceNameLength = 256;
    private const int MaxSamplesPerRequest = 5_000;
    private const int MaxDistinctMetricNamesPerRequest = 200;
    private const int MaxRequestTypeLength = 256;
    private const int MaxDistinctRequestTypesPerRequest = 100;

    private const string RequestTypeAttributeKey = "plaxionmediator.request.type";

    public static IReadOnlyList<MetricSample> Map(
        ExportMetricsServiceRequest request,
        Guid tenantId,
        CumulativeMetricStateStore? cumulativeState = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        // No store supplied (e.g. call sites/tests that don't care about cross-request cumulative
        // tracking) - fall back to a throwaway, request-scoped store so a lone cumulative data
        // point is still treated as its own baseline (delta == raw value) rather than crashing.
        cumulativeState ??= new CumulativeMetricStateStore();

        var samples = new List<MetricSample>();
        var distinctNames = new HashSet<string>(StringComparer.Ordinal);
        var distinctRequestTypes = new HashSet<string>(StringComparer.Ordinal);
        // Total raw data points seen across the request, regardless of whether a cumulative
        // delta ended up producing zero (idle/no-op) - used below to distinguish "empty/malformed
        // payload" (reject) from "valid payload that legitimately carried no new activity" (accept
        // with zero samples).
        var rawDataPointCount = 0;

        foreach (var resourceMetrics in request.ResourceMetrics)
        {
            var serviceName = GetAttribute(resourceMetrics.Resource?.Attributes, "service.name") ?? "unknown-service";
            if (serviceName.Length > MaxServiceNameLength)
            {
                throw new ArgumentException("service.name exceeds maximum length.");
            }

            var environment = GetAttribute(resourceMetrics.Resource?.Attributes, "deployment.environment")
                              ?? GetAttribute(resourceMetrics.Resource?.Attributes, "service.namespace")
                              ?? "unknown";

            foreach (var scopeMetrics in resourceMetrics.ScopeMetrics)
            {
                foreach (var metric in scopeMetrics.Metrics)
                {
                    if (string.IsNullOrWhiteSpace(metric.Name))
                    {
                        throw new ArgumentException("Metric name is required.");
                    }

                    if (metric.Name.Length > MaxMetricNameLength)
                    {
                        throw new ArgumentException("Metric name exceeds maximum length.");
                    }

                    distinctNames.Add(metric.Name);
                    if (distinctNames.Count > MaxDistinctMetricNamesPerRequest)
                    {
                        throw new ArgumentException(
                            $"Request exceeds maximum of {MaxDistinctMetricNamesPerRequest} distinct metric names (cardinality guard).");
                    }

                    switch (metric.DataCase)
                    {
                        case Metric.DataOneofCase.Gauge:
                            // Gauges are instantaneous snapshots, never cumulative - always pass through as-is.
                            rawDataPointCount += metric.Gauge.DataPoints.Count;
                            AddNumberDataPoints(samples, tenantId, serviceName, environment, metric.Name, metric.Gauge.DataPoints, distinctRequestTypes);
                            break;
                        case Metric.DataOneofCase.Sum:
                            rawDataPointCount += metric.Sum.DataPoints.Count;
                            AddSumDataPoints(
                                samples, tenantId, serviceName, environment, metric.Name,
                                metric.Sum.DataPoints, metric.Sum.AggregationTemporality, distinctRequestTypes, cumulativeState);
                            break;
                        case Metric.DataOneofCase.Histogram:
                            rawDataPointCount += metric.Histogram.DataPoints.Count;
                            foreach (var point in metric.Histogram.DataPoints)
                            {
                                var requestType = ExtractRequestType(point.Attributes, distinctRequestTypes);

                                if (metric.Histogram.AggregationTemporality == AggregationTemporality.Cumulative)
                                {
                                    // OTel cumulative histograms report the running total count/sum since
                                    // the series started, so a repeated identical (count, sum) pair across
                                    // exports (e.g. an idle instrumented app) MUST NOT be re-interpreted as a
                                    // fresh duration observation - it represents zero new activity and must
                                    // not produce a MetricSample at all.
                                    var key = SeriesKey(tenantId, serviceName, environment, metric.Name, requestType);
                                    var delta = cumulativeState.ComputeHistogramDelta(key, (long)point.Count, point.HasSum ? point.Sum : point.Count);
                                    var (deltaCount, deltaSum) = delta ?? ((long)point.Count, point.HasSum ? point.Sum : point.Count);

                                    if (deltaCount <= 0)
                                    {
                                        continue;
                                    }

                                    // Approximate this interval's representative duration as the delta's
                                    // average - consistent with the existing (pre-delta) simplification of
                                    // feeding a single scalar per histogram data point into RollupAggregator.
                                    var value = deltaSum / deltaCount;
                                    samples.Add(CreateSample(tenantId, serviceName, environment, metric.Name, value, point.TimeUnixNano, requestType));
                                    EnsureSampleBudget(samples.Count);
                                }
                                else
                                {
                                    // Prefer the histogram sum when present; otherwise count.
                                    double value = point.HasSum ? point.Sum : point.Count;
                                    samples.Add(CreateSample(tenantId, serviceName, environment, metric.Name, value, point.TimeUnixNano, requestType));
                                    EnsureSampleBudget(samples.Count);
                                }
                            }

                            break;
                        case Metric.DataOneofCase.Summary:
                            rawDataPointCount += metric.Summary.DataPoints.Count;
                            foreach (var point in metric.Summary.DataPoints)
                            {
                                // Summary always carries sum + count in the OTLP schema.
                                double value = point.Sum != 0 ? point.Sum : point.Count;
                                var requestType = ExtractRequestType(point.Attributes, distinctRequestTypes);
                                samples.Add(CreateSample(tenantId, serviceName, environment, metric.Name, value, point.TimeUnixNano, requestType));
                                EnsureSampleBudget(samples.Count);
                            }

                            break;
                        case Metric.DataOneofCase.ExponentialHistogram:
                            rawDataPointCount += metric.ExponentialHistogram.DataPoints.Count;
                            foreach (var point in metric.ExponentialHistogram.DataPoints)
                            {
                                double value = point.HasSum ? point.Sum : point.Count;
                                var requestType = ExtractRequestType(point.Attributes, distinctRequestTypes);
                                samples.Add(CreateSample(tenantId, serviceName, environment, metric.Name, value, point.TimeUnixNano, requestType));
                                EnsureSampleBudget(samples.Count);
                            }

                            break;
                    }
                }
            }
        }

        if (rawDataPointCount == 0)
        {
            throw new ArgumentException("At least one metric data point is required.");
        }

        // samples.Count can legitimately be 0 here even though rawDataPointCount > 0: every raw
        // cumulative Sum/Histogram data point reported exactly the same running total as last time
        // (e.g. the instrumented app is idle), so there is genuinely zero new activity to persist -
        // this is a valid, successful ingestion outcome, not a malformed payload.
        return samples;
    }

    private static void AddNumberDataPoints(
        List<MetricSample> samples,
        Guid tenantId,
        string serviceName,
        string environment,
        string metricName,
        IEnumerable<NumberDataPoint> points,
        HashSet<string> distinctRequestTypes)
    {
        foreach (var point in points)
        {
            double value = ReadNumberValue(point);
            var requestType = ExtractRequestType(point.Attributes, distinctRequestTypes);
            samples.Add(CreateSample(tenantId, serviceName, environment, metricName, value, point.TimeUnixNano, requestType));
            EnsureSampleBudget(samples.Count);
        }
    }

    /// <summary>
    /// Maps OTLP Sum (Counter) data points into MetricSamples. Cumulative-temporality points are
    /// converted into per-export deltas via <paramref name="cumulativeState"/> - a data point that
    /// reports the same running total as last time (or a lower delta clamp of exactly zero)
    /// represents zero new executions and is deliberately dropped rather than becoming a sample,
    /// which is what previously made idle periods look like they kept "carrying forward" activity
    /// (every periodic export was turned into a new row regardless of whether the counter moved).
    /// Delta-temporality and unspecified-temporality points already represent a per-interval amount
    /// and are passed through unchanged, matching the pre-existing behavior.
    /// </summary>
    private static void AddSumDataPoints(
        List<MetricSample> samples,
        Guid tenantId,
        string serviceName,
        string environment,
        string metricName,
        IEnumerable<NumberDataPoint> points,
        AggregationTemporality temporality,
        HashSet<string> distinctRequestTypes,
        CumulativeMetricStateStore cumulativeState)
    {
        foreach (var point in points)
        {
            double value = ReadNumberValue(point);
            var requestType = ExtractRequestType(point.Attributes, distinctRequestTypes);

            if (temporality == AggregationTemporality.Cumulative)
            {
                var key = SeriesKey(tenantId, serviceName, environment, metricName, requestType);
                var delta = cumulativeState.ComputeSumDelta(key, value) ?? value;

                if (delta <= 0)
                {
                    continue;
                }

                value = delta;
            }

            samples.Add(CreateSample(tenantId, serviceName, environment, metricName, value, point.TimeUnixNano, requestType));
            EnsureSampleBudget(samples.Count);
        }
    }

    private static double ReadNumberValue(NumberDataPoint point)
    {
        double value = point.ValueCase switch
        {
            NumberDataPoint.ValueOneofCase.AsDouble => point.AsDouble,
            NumberDataPoint.ValueOneofCase.AsInt => point.AsInt,
            _ => double.NaN
        };

        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentException("Metric value must be a finite number.");
        }

        return value;
    }

    /// <summary>Stable per-series key used to track cumulative counter/histogram baselines.</summary>
    private static string SeriesKey(Guid tenantId, string serviceName, string environment, string metricName, string? requestType) =>
        string.Join('\u0001', tenantId, serviceName, environment, metricName, requestType ?? string.Empty);

    private static string? ExtractRequestType(IEnumerable<KeyValue>? attributes, HashSet<string> distinctRequestTypes)
    {
        var requestType = GetAttribute(attributes, RequestTypeAttributeKey);
        if (requestType is null)
        {
            return null;
        }

        if (requestType.Length > MaxRequestTypeLength)
        {
            throw new ArgumentException("RequestType exceeds maximum length.");
        }

        distinctRequestTypes.Add(requestType);
        if (distinctRequestTypes.Count > MaxDistinctRequestTypesPerRequest)
        {
            throw new ArgumentException(
                $"Request exceeds maximum of {MaxDistinctRequestTypesPerRequest} distinct request types (cardinality guard).");
        }

        return requestType;
    }

    private static void EnsureSampleBudget(int count)
    {
        if (count > MaxSamplesPerRequest)
        {
            throw new ArgumentException(
                $"Request exceeds maximum of {MaxSamplesPerRequest} metric points (cardinality guard).");
        }
    }

    private static MetricSample CreateSample(
        Guid tenantId,
        string serviceName,
        string environment,
        string metricName,
        double value,
        ulong timeUnixNano,
        string? requestType = null)
    {
        if (timeUnixNano == 0)
        {
            throw new ArgumentException("Data point timestamp must be positive.");
        }

        // OTLP timestamps are unix nanoseconds.
        var ms = (long)(timeUnixNano / 1_000_000UL);
        return new MetricSample
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ServiceName = serviceName,
            Environment = environment,
            MetricName = metricName,
            Value = value,
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(ms),
            RequestType = requestType
        };
    }

    private static string? GetAttribute(IEnumerable<KeyValue>? attributes, string key)
    {
        if (attributes is null)
        {
            return null;
        }

        foreach (var attr in attributes)
        {
            if (attr.Key == key && attr.Value is not null)
            {
                return attr.Value.ValueCase == AnyValue.ValueOneofCase.StringValue
                    ? attr.Value.StringValue
                    : attr.Value.ToString();
            }
        }

        return null;
    }
}
