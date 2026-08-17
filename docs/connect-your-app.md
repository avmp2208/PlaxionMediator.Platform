# Connect Your App to Platform

## Preferred path: standard OTLP exporter → `POST /v1/metrics`

`PlaxionMediator` and `PlaxionMediator.OpenTelemetry` **are published on nuget.org**
(`1.0.0`) and emit standard `System.Diagnostics.Metrics` instrumentation. Point the
normal OpenTelemetry .NET OTLP HTTP/protobuf exporter at Platform.Ingestion.

### 1. Obtain an API key
- **Local Development:** run `Platform.Ingestion`; the seeded DevTenant key is written
  to `src/Platform.Ingestion/.dev-api-key` (plaintext is **never** logged).
  ```powershell
  Get-Content src/Platform.Ingestion/.dev-api-key
  ```
- **Operator API:** with any valid tenant key, `POST /v1/api-keys` with `{ "name": "my-app" }` and save the returned `plaintextKey` (shown once).

### 2. Configure OpenTelemetry .NET

```csharp
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

services.AddOpenTelemetry()
    .ConfigureResource(r => r
        .AddService("my-service")
        .AddAttributes(new Dictionary<string, object>
        {
            ["deployment.environment"] = "production"
        }))
    .WithMetrics(metrics => metrics
        .AddMeter("PlaxionMediator") // PlaxionMediator.OpenTelemetry meter
        .AddAspNetCoreInstrumentation() // optional
        .AddOtlpExporter(o =>
        {
            o.Protocol = OtlpExportProtocol.HttpProtobuf;
            o.Endpoint = new Uri("http://localhost:5292/v1/metrics");
            o.HttpClientFactory = () =>
            {
                var client = new HttpClient();
                client.DefaultRequestHeaders.Add("X-Api-Key", Environment.GetEnvironmentVariable("PLATFORM_API_KEY")!);
                return client;
            };
        }));
```

Notes:
- **TenantId comes only from the API key**, never from `service.name` or other resource attributes.
- Resource attributes still matter as labels: `service.name` and `deployment.environment` become the Platform service catalog identity.
- Ingest payloads are capped at **1 MiB** and limited to **5 000 samples** / **200 distinct metric names** per request.

### 3. Verify via API
- OTLP success is HTTP `200` with an empty-ish protobuf response body.
- After aggregation runs (Platform.Api hosts `AggregationHostedService`), query:
  - `GET /v1/services`
  - `GET /v1/services/{id}/metrics?range=1h`

### 4. Dashboard
The operator Dashboard is **Angular + Angular Material** (dark mode), fully wired
to `Platform.Api`. It calls `GET /v1/services` / `GET /v1/services/{id}/metrics`
and renders throughput, P50/P95/P99 latency, and failure-rate charts plus a
paginated request-types table, so it can be used to verify real ingested data.
To run it:

```powershell
cd src/platform-dashboard-web
npm install
ng serve   # http://localhost:4200
```

See `docs/dashboard.md` for current status and scope.

## Back-compat bridge: simplified JSON → `POST /v1/ingest`

Still available for curl/demos. Prefer OTLP for real apps.

```powershell
curl -Method POST http://localhost:5292/v1/ingest `
  -Headers @{ "X-Api-Key" = "<your-key>"; "Content-Type" = "application/json" } `
  -Body '{
    "resource": { "serviceName": "my-service", "environment": "staging" },
    "metrics": [ { "metricName": "http.server.duration", "value": 42.5, "timeUnixNano": 1700000000000000000 } ]
  }'
```

## Full contract reference
See `docs/api-contracts.md`.
