using System.Diagnostics.Metrics;

namespace Platform.Ingestion;

/// <summary>
/// Self-observability instrumentation for Platform.Ingestion's request
/// pipeline, using standard <see cref="System.Diagnostics.Metrics"/>
/// primitives (the same style as PlaxionMediator.OpenTelemetry's own
/// Meter-based instrumentation). A single shared Meter is registered as a
/// DI singleton and used at controller boundaries (IngestController,
/// OtlpMetricsController). Intentionally separate from the ingested
/// telemetry pipeline itself - these measurements are emitted via the
/// standard .NET Meter API only and are never written back into
/// Platform.Storage / re-ingested through POST /v1/metrics.
/// </summary>
public sealed class IngestionMetrics
{
    public const string MeterName = "Platform.Ingestion";

    public Meter Meter { get; }
    public Counter<long> IngestRequests { get; }
    public Histogram<double> IngestLatencyMs { get; }
    public Counter<long> RejectedTelemetry { get; }

    public IngestionMetrics()
    {
        Meter = new Meter(MeterName);
        IngestRequests = Meter.CreateCounter<long>(
            "platform.ingestion.requests",
            description: "Count of ingestion requests, tagged by outcome (success/failure).");
        IngestLatencyMs = Meter.CreateHistogram<double>(
            "platform.ingestion.latency",
            unit: "ms",
            description: "Latency of ingestion requests.");
        RejectedTelemetry = Meter.CreateCounter<long>(
            "platform.ingestion.rejected",
            description: "Count of rejected/invalid telemetry payloads.");
    }
}
