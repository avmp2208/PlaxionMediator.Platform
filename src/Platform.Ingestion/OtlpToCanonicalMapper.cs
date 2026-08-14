using Platform.Storage;

namespace Platform.Ingestion;

public static class OtlpToCanonicalMapper
{
    private const int MaxMetricNameLength = 256;
    private const int MaxServiceNameLength = 256;
    private const int MaxSamplesPerRequest = 5_000;
    private const int MaxDistinctMetricNamesPerRequest = 200;

    /// <summary>
    /// Validates and maps an ingestion transport request into canonical
    /// MetricSample records for the given (trusted) tenant. Throws
    /// ArgumentException for malformed / high-cardinality input so callers
    /// can return 400 Bad Request.
    /// </summary>
    public static IReadOnlyList<MetricSample> Map(IngestRequest request, Guid tenantId)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Resource.ServiceName))
        {
            throw new ArgumentException("Resource.ServiceName is required.");
        }

        if (request.Resource.ServiceName.Length > MaxServiceNameLength)
        {
            throw new ArgumentException("Resource.ServiceName exceeds maximum length.");
        }

        if (request.Metrics is null || request.Metrics.Count == 0)
        {
            throw new ArgumentException("At least one metric point is required.");
        }

        if (request.Metrics.Count > MaxSamplesPerRequest)
        {
            throw new ArgumentException(
                $"Request exceeds maximum of {MaxSamplesPerRequest} metric points (cardinality guard).");
        }

        var environment = string.IsNullOrWhiteSpace(request.Resource.Environment)
            ? "unknown"
            : request.Resource.Environment;

        var distinctNames = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<MetricSample>(request.Metrics.Count);
        foreach (var point in request.Metrics)
        {
            if (string.IsNullOrWhiteSpace(point.MetricName))
            {
                throw new ArgumentException("MetricName is required for every metric point.");
            }

            if (point.MetricName.Length > MaxMetricNameLength)
            {
                throw new ArgumentException("MetricName exceeds maximum length (possible high-cardinality abuse).");
            }

            if (double.IsNaN(point.Value) || double.IsInfinity(point.Value))
            {
                throw new ArgumentException("Metric value must be a finite number.");
            }

            if (point.TimeUnixNano <= 0)
            {
                throw new ArgumentException("TimeUnixNano must be a positive value.");
            }

            distinctNames.Add(point.MetricName);
            if (distinctNames.Count > MaxDistinctMetricNamesPerRequest)
            {
                throw new ArgumentException(
                    $"Request exceeds maximum of {MaxDistinctMetricNamesPerRequest} distinct metric names (cardinality guard).");
            }

            result.Add(new MetricSample
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ServiceName = request.Resource.ServiceName,
                Environment = environment,
                MetricName = point.MetricName,
                Value = point.Value,
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(point.TimeUnixNano / 1_000_000)
            });
        }

        return result;
    }
}
