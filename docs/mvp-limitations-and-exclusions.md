# MVP Limitations and Post-MVP Exclusions

The frozen, authoritative statement of MVP scope (supported / not
supported / self-hosted preview / future) lives in the documentation
repository: `platform-mvp-contract.md`. This document is the
implementation-level detail behind that contract.

## MVP limitations (known gaps in what's implemented)

- Development defaults to in-memory storage; durable deployment requires
  explicit `Storage:Provider=Sqlite|PostgreSql|MySql` and a shared
  `ConnectionStrings:Platform`. Production/Staging/QA refuse
  `InMemory`/`Sqlite`.
- OTLP support is **HTTP + protobuf only** (`POST /v1/metrics`). OTLP/gRPC
  and OTLP/HTTP+JSON are not implemented.
- **No trace ingestion.** Only metrics are processed — no OTLP trace
  endpoint stores spans. (A `docs/deployment/docker-compose.md` example
  shows a `/v1/traces` OTel exporter configuration snippet for a reachability
  smoke test only — this documents the ingestion service's URL shape, not a
  supported trace-processing capability. Do not read it as trace support.)
- Histogram/Summary/ExponentialHistogram datapoints are reduced to a single
  representative value (sum if present, else count) in the canonical model —
  full bucket histograms are not stored.
- Rate limiting: implemented for the anonymous `POST /v1/tenants` bootstrap
  endpoint only (5 requests/minute per client IP, fixed-window, 429 on
  excess); other endpoints rely on API-key/OIDC auth plus body size +
  cardinality guards, not IP-based rate limiting.
- Platform self-observability: implemented via standard .NET
  `Meter`/`Counter`/`Histogram` instruments (`IngestionMetrics`,
  `AggregationMetrics`, `ApiMetrics`) — not yet dogfooding the
  PlaxionMediator/OTLP exporter path for Platform's own telemetry, and no
  scrape endpoint/dashboard is wired up for these metrics beyond in-process
  listeners used by tests.
- Single-region, single-instance assumption; no HA/scaling story beyond
  running multiple stateless API/Ingestion replicas against one database.
- Durable storage supports SQLite, PostgreSQL, and MySQL (via
  `Storage:Provider`), but only **SQLite and PostgreSQL have EF Core
  migrations and conformance-test coverage**; MySQL is `EnsureCreated`-only,
  has no migrations assembly, and has no dedicated conformance tests. The
  Helm chart restricts `database.provider` to `PostgreSql` only, so MySQL is
  not a deployable production target through the official chart regardless
  of its application-level support.
- **PostgreSQL conformance is now verified against a live server** (12
  `PostgreSqlConformanceTests` covering tenant isolation, rollup
  upsert/dedup, retention, cancellation, round-trip and restart persistence,
  time-range filtering, cross-tenant catalog isolation — all passing as of
  2026-08-26) and wired into CI via a `postgres:16-alpine` service
  container. This was previously a real coverage gap (tests existed but
  auto-skipped without a live server, and CI provisioned none); it is
  closed. MySQL conformance against a live server remains unverified.
- OIDC is verified generically (protocol-level correctness against a
  synthetic in-test JWT scheme) but **not** against any specific live
  identity provider process in this repository's own automated test suite.
  Keycloak and Duende IdentityServer have worked-example configuration docs
  and Helm example values, which is a lower bar than an automated
  integration test.
- Coarse two-tier tenant authorization (Owner/Member) — intentionally not a
  full RBAC framework; see `docs/tenant-invitations-and-roles.md`.
- No invitation email delivery — invitees must be told out-of-band which
  email address to sign in with.
- No standalone API-key management page or "remove existing member" action
  in the Dashboard UI — both underlying APIs exist without a dedicated
  screen.
- No browser end-to-end (Cypress/Playwright) test coverage — unit/component
  tests only.

## What is implemented (not limitations anymore)

- **Generic OIDC human authentication** (Authorization Code + PKCE,
  provider-neutral discovery via `Authentication:Authority`/`Audience`) for
  the Dashboard and `Platform.Api`, layered on top of — not replacing — the
  original API-key telemetry ingestion auth. See
  `docs/deployment/authentication.md`.
- **TenantMembership with Owner/Member roles**, invitation lifecycle
  (create/list/revoke, just-in-time claim, duplicate protection), and
  **PlatformAdministrator break-glass** authorization (default `Disabled`).
  See `docs/tenant-invitations-and-roles.md`.
- **Angular Dashboard** wired to `Platform.Api`, OIDC-first with a legacy
  anonymous fallback, including real throughput/P50-P95-P99-latency/
  failure-rate time-series charts (shared bucket timeline, correct idle/gap
  semantics, no carry-forward), a server-side paginated request-types
  table, and an invitations management page. See `docs/dashboard.md`.
- Real OTLP/HTTP protobuf receiver with `TenantId`-from-API-key only.
- Provider-agnostic storage with SQLite **and PostgreSQL** reference
  providers, both with EF Core migrations and conformance tests — the
  PostgreSQL suite now runs against a live server in CI, not just SQLite.
- Shared SQLite multi-host integration proof.
- Operator HTTP API for create/list/revoke API keys (plaintext once, dual
  API-key/JWT-Owner auth).
- `/v1/ingest` retained as documented dev/back-compat bridge.
- Ingest body size limits (1 MiB) on `/v1/metrics` and `/v1/ingest`.
- Cardinality guards (max samples + distinct metric names per request; name
  length caps).
- API-key plaintext never written to logs.
- Health endpoints `/health`/`/healthz` (liveness) and `/health/ready`
  (DB-aware readiness) on Ingestion and Api, matched exactly by the Helm
  chart's probe paths.
- Tenant-query isolation on the Dashboard-facing read endpoints
  (`GET /v1/tenants/{tenantId}/services*`): 404 (not 403) on cross-tenant
  access, avoiding tenant-ID enumeration; 401 on missing/invalid auth.
- Production migrations for SQLite and PostgreSQL via a shared `--migrate`
  CLI flag, invoked identically by the Helm chart's migration Job.
- Production fail-fast guards (Environment=Production + anonymous auth
  without explicit opt-in; InMemory/Sqlite storage outside
  Development/Testing) confirmed in code.
- Single multi-role container image and an official Helm chart
  (`charts/plaxion-platform` in the `plaxion-platform` repository) with 8
  example deployment profiles, 4 of which are covered by the chart's own CI
  matrix and 4 (all OIDC variants) currently render-verified manually only.

## Explicit post-MVP exclusions (out of scope for this repo)

The following are intentionally **not** implemented, per the strategy-v2
Platform documents (Docs C/D/E/F) and the frozen MVP contract:

- Per-behavior/handler-level timing ("semantic mediator observability" —
  the strategy documents' stated core V2 differentiator).
- SLOs and SLO burn-rate tracking, alerting/notification pipelines (Doc C).
- Distributed tracing / trace ingestion / trace explorer (Doc C/D).
- Advanced diagnostics, cross-instance correlation, anomaly detection
  (Doc D).
- Policy and compliance engines, service topology mapping (Doc E).
- Kubernetes-native integrations/operators beyond the Helm chart itself
  (Doc F).
- The PlaxionMediator CLI (Docs G-J) and any CLI packaging/distribution
  work.
- Full RBAC beyond the Owner/Member two-tier model.
- Invitation email delivery.
- Billing and licensing enforcement.
- Real-time streaming aggregation (aggregation is scheduled/batch only).

**Correction to earlier drafts of this document:** an earlier version
stated "SSO/OAuth/enterprise RBAC in `Platform.Identity`... only simple
tenant-scoped API keys are in scope for the MVP." That statement is false
as of the current implementation — generic OIDC human authentication,
tenant memberships, and invitations are implemented and tested (see above).
API keys remain in scope, unchanged, specifically for telemetry ingestion.
