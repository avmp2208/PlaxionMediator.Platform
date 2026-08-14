# API Contracts

## Authentication
All endpoints below require an `X-Api-Key: <key>` header with a valid, non-revoked API key
issued by `Platform.Identity.ApiKeyService` for a tenant. `TenantId` is resolved exclusively from
this trusted, server-side lookup — it is never accepted from the client (e.g. request body,
query string, OTLP resource attributes, or any other client-supplied field).

Missing/invalid/revoked keys return `401 Unauthorized`.

---

## `POST /v1/metrics` (Platform.Ingestion) — **preferred / production**

Standard **OTLP/HTTP + protobuf** metrics export endpoint.

- Path: `/v1/metrics` (OTLP metrics signal path)
- Content-Type: `application/x-protobuf` (also accepts `application/protobuf`)
- Content-Encoding: optional `gzip`
- Body: protobuf-encoded `opentelemetry.proto.collector.metrics.v1.ExportMetricsServiceRequest`
- Auth: `X-Api-Key` (TenantId from key only; resource `service.name` / `deployment.environment` are labels, not tenancy)
- Success: `200 OK` with protobuf `ExportMetricsServiceResponse`
- Validation failures: `400 Bad Request`
- Body size limit: **1 MiB** (`[RequestSizeLimit]` + Kestrel `MaxRequestBodySize`)
- Cardinality guards (mapper → 400): max **5 000** samples/request, max **200** distinct metric names/request, metric/service name length ≤ 256

Supported metric data cases: Gauge, Sum, Histogram, Summary, ExponentialHistogram
(histograms/summaries map a representative numeric value — sum if present, else count).

### Client configuration (OpenTelemetry .NET)

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("PlaxionMediator") // and/or your app meters
        .AddOtlpExporter(o =>
        {
            o.Protocol = OtlpExportProtocol.HttpProtobuf;
            o.Endpoint = new Uri("http://localhost:<ingestion-port>/v1/metrics");
            o.HttpClientFactory = () =>
            {
                var c = new HttpClient();
                c.DefaultRequestHeaders.Add("X-Api-Key", "<your-key>");
                return c;
            };
        }));
```

Environment-variable style (if using a collector or custom header injector):
- `OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf`
- `OTEL_EXPORTER_OTLP_METRICS_ENDPOINT=http://host:port/v1/metrics`
- API key must still be attached as `X-Api-Key` (custom header).

---

## `POST /v1/ingest` (Platform.Ingestion) — **dev / back-compat bridge**

Simplified OTLP-inspired JSON transport. **Kept intentionally** for local demos and
existing tests; production apps should prefer `/v1/metrics`.

Request body:
```json
{
  "resource": {
    "serviceName": "checkout-service",
    "environment": "production"
  },
  "metrics": [
    { "metricName": "http.server.duration", "value": 123.4, "timeUnixNano": 1700000000000000000 }
  ]
}
```

Validation rules (violations return `400 Bad Request`):
- `resource.serviceName` required, non-blank, max 256 chars.
- `metrics` must contain at least one entry and at most **5 000** points.
- At most **200** distinct `metricName` values per request (cardinality guard).
- Each `metricName` required, non-blank, max 256 chars (cardinality guard).
- Each `value` must be a finite number (no `NaN`/`Infinity`).
- Each `timeUnixNano` must be a positive value.
- `resource.environment` defaults to `"unknown"` if blank.
- Body size limit: **1 MiB**.

Response: `202 Accepted` with `{ "accepted": <count> }`.

---

## API key admin (Platform.Ingestion)

All routes require a valid tenant API key; operations are scoped to that tenant.

### `POST /v1/api-keys`
Body: `{ "name": "ci-bot" }`  
Response `201 Created`:
```json
{ "id": "guid", "name": "ci-bot", "plaintextKey": "pk_..." }
```
**Plaintext is returned only in this response** and is never persisted or listed later.

### `GET /v1/api-keys`
Response `200 OK`: list of `{ id, name, createdAt, isActive, revokedAt }` (no secrets).

### `DELETE /v1/api-keys/{id}`
Revokes the key if it belongs to the caller’s tenant.  
Response `204 No Content`, or `404` if unknown/other-tenant (no existence leak).

---

## `GET /v1/services` (Platform.Api)

Returns the list of services registered under the authenticated caller’s tenant only.

Response `200 OK`:
```json
[
  { "id": "guid", "name": "checkout-service", "environment": "production" }
]
```

## `GET /v1/services/{id}/metrics?range=...` (Platform.Api)

Returns `AggregateRollup` records for the given service id, scoped to the authenticated tenant.
If `{id}` belongs to a different tenant (or doesn't exist), the endpoint returns `404 Not Found`
— it never distinguishes "not found" from "not yours" (no existence leakage).

`range` examples: `15m`, `1h`, `24h`, `7d` (default `1h`).

Response `200 OK`: list of rollups, each with `bucketStart`, `bucketEnd`, `metricName`,
`count`, `min`, `max`, `avg`, `p50`, `p95`, `p99`.

---

## Health (Platform.Ingestion and Platform.Api)

Unauthenticated liveness probes:

- `GET /health` → `200 OK`
- `GET /healthz` → `200 OK`
