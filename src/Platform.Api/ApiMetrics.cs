using System.Diagnostics.Metrics;

namespace Platform.Api;

/// <summary>
/// Self-observability instrumentation for Platform.Api's query surface,
/// using standard <see cref="System.Diagnostics.Metrics"/> primitives (the
/// same style as PlaxionMediator.OpenTelemetry's own Meter-based
/// instrumentation). A single shared Meter is registered as a DI singleton
/// and used at controller boundaries. This is intentionally separate from
/// the ingested telemetry pipeline (POST /v1/metrics) - it is never fed back
/// into Platform's own storage.
/// </summary>
public sealed class ApiMetrics
{
    public const string MeterName = "Platform.Api";

    public Meter Meter { get; }
    public Histogram<double> QueryLatencyMs { get; }
    public Counter<long> QueryFailures { get; }

    public ApiMetrics()
    {
        Meter = new Meter(MeterName);
        QueryLatencyMs = Meter.CreateHistogram<double>(
            "platform.api.query.latency",
            unit: "ms",
            description: "Latency of query endpoints.");
        QueryFailures = Meter.CreateCounter<long>(
            "platform.api.query.failures",
            description: "Count of failed query requests.");
    }
}
