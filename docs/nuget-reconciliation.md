### NuGet / OTel reconciliation findings

- **`PlaxionMediator` and `PlaxionMediator.OpenTelemetry` ARE published on nuget.org** at `1.0.0`.
- **Standard OpenTelemetry .NET SDK is current at 1.17.0** (`OpenTelemetry`,
  `OpenTelemetry.Exporter.OpenTelemetryProtocol`, etc.).
- **API surface:** `PlaxionMediator.OpenTelemetry` emits standard
  `System.Diagnostics.Metrics` / `Activity` instrumentation via
  `OpenTelemetryPipelineObserver` — no proprietary wire protocol.
- **Platform OTLP receiver (implemented):** `POST /v1/metrics` uses official
  `opentelemetry-proto` message types (compiled with Grpc.Tools + Google.Protobuf)
  and accepts OTLP/HTTP protobuf from `OpenTelemetry.Exporter.OpenTelemetryProtocol`.
- **Verification:**
  - `PlaxionMediatorOpenTelemetryIngestionTests` — package instruments → simplified `/v1/ingest`.
  - `OtlpMetricsEndpointTests.RealOpenTelemetrySdk_OtlpHttpExporter_WithPlaxionMediator_PersistsMetric`
    — real SDK MeterProvider + OTLP exporter configuration + PlaxionMediator observer →
    persistence via `/v1/metrics`.
- **`/v1/ingest` simplified JSON remains** as a documented back-compat/dev bridge only.
