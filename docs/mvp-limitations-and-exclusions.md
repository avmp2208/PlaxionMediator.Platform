# MVP Limitations and Post-MVP Exclusions

## MVP limitations (known gaps in what's implemented)
- **Dashboard was re-platformed from Blazor to Angular + Angular Material and is
  currently UI-only** (dark mode, mock data) — not yet wired to `Platform.Api`.
  No charts/graphs yet (tables only). See `docs/dashboard.md`.
- Development defaults to in-memory storage; multi-process durability requires explicit
  `Storage:Provider=Sqlite` and a shared `ConnectionStrings:Platform` on Ingestion + Api.
- OTLP support is **HTTP + protobuf only** (`POST /v1/metrics`). OTLP/gRPC and OTLP/HTTP+JSON
  are not implemented.
- Histogram/Summary/ExponentialHistogram datapoints are reduced to a single representative
  value (sum if present, else count) in the canonical model — full bucket histograms are not stored.
- Rate limiting: implemented for the anonymous `POST /v1/tenants` bootstrap endpoint only (5 requests/minute per client IP, fixed-window, 429 on excess); other endpoints rely on API-key auth plus body size + cardinality guards, not IP-based rate limiting.
- Platform self-observability: implemented via standard .NET `Meter`/`Counter`/`Histogram` instruments (`IngestionMetrics`, `AggregationMetrics`, `ApiMetrics`) - not yet dogfooding the PlaxionMediator/OTLP exporter path for Platform's own telemetry, and no scrape endpoint/dashboard is wired up for these metrics beyond in-process listeners used by tests.
- Single-region, single-instance assumption; no HA/scaling story.
- Durable storage supports SQLite, MySQL and PostgreSQL (selected via `Storage:Provider`); MySQL/PostgreSQL are not yet covered by conformance tests against real servers.

## What is implemented (not limitations anymore)
- Real OTLP/HTTP protobuf receiver with TenantId-from-API-key only.
- Provider-agnostic storage + SQLite reference provider with conformance tests.
- Shared SQLite multi-host integration proof.
- Operator HTTP API for create/list/revoke API keys (plaintext once).
- `/v1/ingest` retained as documented dev/back-compat bridge.
- Ingest body size limits (1 MiB) on `/v1/metrics` and `/v1/ingest`.
- Cardinality guards (max samples + distinct metric names per request; name length caps).
- API-key plaintext never written to logs (dev seed → `.dev-api-key` file only).
- Health endpoints `/health` and `/healthz` on Ingestion and Api.
- Tenant-query isolation on the Dashboard-facing read endpoints: `GET /v1/tenants/{tenantId}/services`
  and `GET /v1/tenants/{tenantId}/services/{id}/metrics` require a valid `X-Api-Key` (`[Authorize]`)
  and derive/validate `TenantId` from the authenticated key, never from the URL segment alone — a
  mismatch resolves to 404 (not 403), and a missing/invalid key resolves to 401. This closes a
  previously release-blocking gap where any caller could read another tenant's services/metrics by
  guessing/knowing its `TenantId`.

## Explicit post-MVP exclusions (out of scope for this repo, per Docs C/D/E/F)
The following are intentionally **not** implemented and were not read/designed against in this
repository, per the original task scope:
- SLOs and SLO burn-rate tracking (Doc C)
- Alerting/notification pipelines (Doc C)
- Distributed tracing / trace explorer (Doc D)
- Advanced diagnostics and anomaly detection (Doc D)
- Policy and compliance engines (Doc E)
- Service topology mapping (Doc E)
- Kubernetes-native integrations/operators (Doc F)
- The PlaxionMediator CLI (Docs G-J) and any CLI packaging/distribution work
- SSO/OAuth/enterprise RBAC in `Platform.Identity` (only simple tenant-scoped API keys are in
  scope for the MVP)
- Real-time streaming aggregation (aggregation is scheduled/batch only, per Doc B)
