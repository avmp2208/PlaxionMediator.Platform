namespace Platform.Ingestion;

/// <summary>
/// Simplified OTLP-compatible transport schema for a single metric data
/// point. Intentionally separate from the canonical Platform.Storage
/// persistence model (Platform.Storage.MetricSample) - mapping is done in
/// OtlpToCanonicalMapper.
/// </summary>
public sealed class IngestMetricPoint
{
    public string MetricName { get; set; } = string.Empty;
    public double Value { get; set; }
    public long TimeUnixNano { get; set; }
}

public sealed class IngestResource
{
    public string ServiceName { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
}

public sealed class IngestRequest
{
    public IngestResource Resource { get; set; } = new();
    public List<IngestMetricPoint> Metrics { get; set; } = new();
}
