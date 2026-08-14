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
- No request rate limiting (body size + cardinality guards only).
- No self-observability (Platform dogfooding its own metrics/traces).
- Single-region, single-instance assumption; no HA/scaling story.
- SQLite is the only durable provider; Postgres provider not built (interfaces ready).

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
