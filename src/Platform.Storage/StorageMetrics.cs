using System.Diagnostics.Metrics;

namespace Platform.Storage;

/// <summary>
/// Self-observability instrumentation for the storage layer, using standard
/// <see cref="System.Diagnostics.Metrics"/> primitives (the same style as
/// PlaxionMediator.OpenTelemetry's own Meter-based instrumentation). A
/// single shared Meter is registered as a DI singleton and used by any
/// IMetricSampleStore/IAggregateRollupStore implementation (e.g. the
/// EF Core-backed provider in Platform.Storage.Relational, which keeps all
/// EF Core specifics internal and only depends on this provider-neutral
/// Meter). Intentionally separate from the ingested telemetry pipeline.
/// </summary>
public sealed class StorageMetrics
{
    public const string MeterName = "Platform.Storage";

    public Meter Meter { get; }
    public Histogram<double> OperationLatencyMs { get; }
    public Counter<long> Failures { get; }

    public StorageMetrics()
    {
        Meter = new Meter(MeterName);
        OperationLatencyMs = Meter.CreateHistogram<double>(
            "platform.storage.operation.latency",
            unit: "ms",
            description: "Latency of storage operations (add/query/delete).");
        Failures = Meter.CreateCounter<long>(
            "platform.storage.failures",
            description: "Count of failed storage operations.");
    }
}
