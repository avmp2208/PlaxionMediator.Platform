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

    public static IReadOnlyList<MetricSample> Map(ExportMetricsServiceRequest request, Guid tenantId)
    {
        ArgumentNullException.ThrowIfNull(request);

        var samples = new List<MetricSample>();
        var distinctNames = new HashSet<string>(StringComparer.Ordinal);
        var distinctRequestTypes = new HashSet<string>(StringComparer.Ordinal);

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
                            AddNumberDataPoints(samples, tenantId, serviceName, environment, metric.Name, metric.Gauge.DataPoints, distinctRequestTypes);
                            break;
                        case Metric.DataOneofCase.Sum:
                            AddNumberDataPoints(samples, tenantId, serviceName, environment, metric.Name, metric.Sum.DataPoints, distinctRequestTypes);
                            break;
                        case Metric.DataOneofCase.Histogram:
                            foreach (var point in metric.Histogram.DataPoints)
                            {
                                // Prefer the histogram sum when present; otherwise count.
                                double value = point.HasSum ? point.Sum : point.Count;
                                var requestType = ExtractRequestType(point.Attributes, distinctRequestTypes);
                                samples.Add(CreateSample(tenantId, serviceName, environment, metric.Name, value, point.TimeUnixNano, requestType));
                                EnsureSampleBudget(samples.Count);
                            }

                            break;
                        case Metric.DataOneofCase.Summary:
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

        if (samples.Count == 0)
        {
            throw new ArgumentException("At least one metric data point is required.");
        }

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

            var requestType = ExtractRequestType(point.Attributes, distinctRequestTypes);
            samples.Add(CreateSample(tenantId, serviceName, environment, metricName, value, point.TimeUnixNano, requestType));
            EnsureSampleBudget(samples.Count);
        }
    }

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
