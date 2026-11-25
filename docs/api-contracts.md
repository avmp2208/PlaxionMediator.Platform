# API Contracts

> **Scope note (2026-11-25):** this doc originally covered only the machine/ingestion surface,
> written before OIDC login shipped. Platform.Api now has two independent authentication
> surfaces — see below — and a human-facing tenant/invitation surface (added with multi-tenant
> email invitations). Both are documented here now.

## Authentication — two independent surfaces

**Machine / ingestion surface** — all `Platform.Ingestion` endpoints and the `/v1/api-keys`,
`/v1/services*` endpoints below require an `X-Api-Key: <key>` header with a valid, non-revoked
API key issued by `Platform.Identity.ApiKeyService` for a tenant. `TenantId` is resolved
exclusively from this trusted, server-side lookup — it is never accepted from the client (e.g.
request body, query string, OTLP resource attributes, or any other client-supplied field).
Missing/invalid/revoked keys return `401 Unauthorized`.

**Human / Dashboard surface (OIDC)** — the tenant and invitation endpoints below require a JWT
Bearer token from your configured OIDC provider (Authorization Code + PKCE from the Dashboard
SPA). This is unrelated to, and never substitutes for, the API-key surface — ingestion always
uses `X-Api-Key` regardless of whether OIDC is enabled. See
[tenant-invitations-and-roles.md](./tenant-invitations-and-roles.md) for the authorization model.

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

Additional cardinality guard: if a data point carries the `plaxionmediator.request.type` attribute
(string value), it is extracted into `MetricSample.RequestType`; at most **100** distinct values
are accepted per request and each value is capped at 256 chars — otherwise `400 Bad Request`.

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
- Optional `requestType` per point: at most **100** distinct values per request, max 256 chars each (cardinality guard); mirrors the `plaxionmediator.request.type` OTLP attribute for parity between the two ingestion paths.
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

Response `200 OK`: list of `MetricRollupResponse` rollups, each with:

| Field | Type | Notes |
|---|---|---|
| `bucketStart` | `DateTimeOffset` | Inclusive start of the rollup bucket. |
| `bucketEnd` | `DateTimeOffset` | Exclusive end of the rollup bucket. |
| `metricName` | `string` | e.g. `plaxionmediator.request.count`, `plaxionmediator.request.duration`, `plaxionmediator.pipeline_exception.count`, or any app-specific metric name. |
| `count` | `int` | Number of raw samples that fed this rollup. |
| `min` / `max` / `avg` | `double` | Basic stats over the raw sample values in the bucket. |
| `p50` / `p95` / `p99` | `double` | Nearest-rank percentiles over the raw sample values in the bucket. |
| `requestType` | `string?` | Optional request-type dimension (the CLR request type name), populated only for `PlaxionMediator.OpenTelemetry` metrics (`plaxionmediator.request.count`, `.request.duration`, `.pipeline_exception.count`). `null` for metrics without this dimension (e.g. generic gauges/counters). |

Rollups are **not** filtered/collapsed server-side by `requestType` — the response includes both
the aggregate-without-request-type rows (when applicable) and any per-request-type rows for the
same metric/bucket; the caller (dashboard) groups/filters client-side.

### Deriving request-type dashboards from this endpoint
With `requestType` present, a client can build, entirely from this one endpoint's response,
tenant-scoped:
- **Throughput over time**: group rows where `metricName == "plaxionmediator.request.count"` by
  `bucketStart` (optionally further by `requestType`) and plot `count`/`sum` over time.
- **Latency (P50/P95/P99) over time**: use rows where
  `metricName == "plaxionmediator.request.duration"`, plotting `p50`/`p95`/`p99` over `bucketStart`,
  optionally split by `requestType`.
- **Failure/error rate**: for the same `requestType` + `bucketStart`, divide the rollup `count` for
  `metricName == "plaxionmediator.pipeline_exception.count"` by the rollup `count` for
  `metricName == "plaxionmediator.request.count"`. This metric-name-based join is correct as long as
  the client matches on the full `(requestType, bucketStart)` key (both metrics are rolled up from the
  same bucketing/grouping logic in `RollupAggregator`, so buckets line up exactly); if a bucket has
  request-count rows but no exception rows for a given `requestType`, treat the failure rate as `0`,
  not "unknown".
- **Request-type ranking table**: group rows where `metricName == "plaxionmediator.request.count"` by
  `requestType` over the selected range, summing `count` for "most active", and separately taking the
  max `p95`/`p99` from `plaxionmediator.request.duration` rows per `requestType` for "slowest".

**Known gap**: there is no handler-type (as opposed to request-type) dimension anywhere in the
published `PlaxionMediator.OpenTelemetry` OSS package — all measurements are tagged only with
`plaxionmediator.request.type` (the request CLR type name), never a handler identifier. Handler-level
ranking is therefore not implementable without an upstream OSS change and is explicitly out of scope
here.

---

## Human / Dashboard surface — tenants, memberships, invitations (Platform.Api, OIDC)

All routes in this section use `[Authorize]` against the configured OIDC Bearer token, not
`X-Api-Key`. `TenantId` is still never accepted from the client — it comes from path parameters
validated against the caller's claimed memberships, or from server-side JIT-claim matching.

### `GET /v1/platform/config` — unauthenticated

Dashboard bootstrap endpoint, called before login. Returns the OIDC authority/client/scopes
configuration the SPA needs to start an Authorization Code + PKCE flow. Safe to expose publicly —
contains no secrets (the OIDC client is a public SPA client by design).

### `GET /v1/tenants`

Policy: `JwtBearer` (any authenticated user). Lists tenants the caller currently has a
`TenantMembership` row for. On a matching unclaimed invitation (email claim match), the caller is
JIT-claimed into a `Member` membership before the list is returned — no separate "accept
invitation" step exists.

### `POST /v1/tenants`

Policy: `TenantCreation` — configurable via `authentication.authorization.tenantCreation.mode`
(`AnyAuthenticatedUser` | `Role` | `Claim`; production recommendation is `Role`). Self-service
tenant creation; the creating user becomes that tenant's first `Owner`.

### `POST /v1/invitations`

Policy: `JwtBearer`. Body: `{ email, tenantIds[] }`. Caller must be `Owner` of every tenant in
`tenantIds`. Creates one invitation per tenant atomically; no email is sent — the invitee is
JIT-claimed on their next `GET /v1/tenants` call if their validated JWT's email claim matches.

### `GET /v1/invitations`

Policy: `JwtBearer`. Lists invitations the caller can see (scoped to tenants they own).

### `POST /v1/invitations/{invitationId}/revoke`

Policy: `JwtBearer`. Revokes a pending invitation; caller must own the invitation's tenant.

### `POST /v1/tenants/{tenantId}/memberships`

Policy: `PlatformAdministrator` (break-glass only, `Disabled` by default — see
`authentication.authorization.platformAdministrator.mode`). Explicit membership reconciliation
for pre-OIDC legacy tenants with zero membership rows. Not part of normal onboarding, which uses
Owner-issued invitations above.

---

## Health (Platform.Ingestion and Platform.Api)

Unauthenticated liveness probes:

- `GET /health` → `200 OK`
- `GET /healthz` → `200 OK`
