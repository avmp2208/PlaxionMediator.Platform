using System.Diagnostics.Metrics;

namespace Platform.Aggregation;

/// <summary>
/// Self-observability instrumentation for the aggregation scheduled job,
/// using standard <see cref="System.Diagnostics.Metrics"/> primitives (the
/// same style as PlaxionMediator.OpenTelemetry's own Meter-based
/// instrumentation). A single shared Meter is registered as a DI singleton
/// and used at the job's execution boundary. This is intentionally separate
/// from the ingested telemetry pipeline (POST /v1/metrics) - it is never fed
/// back into Platform's own storage.
/// </summary>
public sealed class AggregationMetrics
{
    public const string MeterName = "Platform.Aggregation";

    public Meter Meter { get; }
    public Histogram<double> JobDurationMs { get; }
    public Counter<long> Failures { get; }

    public AggregationMetrics()
    {
        Meter = new Meter(MeterName);
        JobDurationMs = Meter.CreateHistogram<double>(
            "platform.aggregation.job.duration",
            unit: "ms",
            description: "Duration of each aggregation job run.");
        Failures = Meter.CreateCounter<long>(
            "platform.aggregation.failures",
            description: "Count of failed aggregation job runs (includes lag: a failed run leaves samples unaggregated).");
    }
}
