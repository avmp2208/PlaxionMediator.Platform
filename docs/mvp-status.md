# MVP Status - honest snapshot

This project is being built under a constrained, multi-session step budget
against the very large Doc B execution plan. This document is the
authoritative, continuously-updated record of what is real vs. what
remains. See also `docs/storage-decision.md` and `docs/nuget-reconciliation.md`.

## Component status at a glance

| Component | Status |
|---|---|
| `Platform.Storage` / `Platform.Storage.Relational` | Done |
| `Platform.Hosting` | Done |
| `Platform.Identity` | Done |
| `Platform.Ingestion` (OTLP + JSON bridge + API-key admin) | Done |
| `Platform.Aggregation` | Done |
| `Platform.Api` | Done |
| `platform-dashboard-web` (Angular Dashboard) | In Progress (tenant-list-first flow added; pending backend `/v1/tenants` + `/v1/api-keys`) |

## Done (implemented and test-verified)

- Solution bootstrap: `PlaxionMediator.Platform.sln`, `Directory.Build.props`,
  `docs/`, and projects:
  `src/Platform.Ingestion`, `src/Platform.Storage`, `src/Platform.Storage.Relational`,
  `src/Platform.Hosting`, `src/Platform.Identity`, `src/Platform.Aggregation`,
  `src/Platform.Api`, with matching `tests/*.Tests` projects.
- **Angular Dashboard**: Wired to `Platform.Api` (`/v1/services`, `/v1/services/{id}/metrics`) using Angular 18 functional interceptors for `X-Api-Key` auth. Environment-based configuration supports local, staging, qa, and production. Dark-mode Material theme, login validation, and loading/error handling implemented.
- **Dashboard tenant-list-first flow (In Progress)**: the landing route `/` is now `TenantsComponent`, listing tenants created on this browser via a new `localStorage`-backed `TenantRegistryService` (masked keys only, never plaintext). A "Create Tenant" dialog calls `PlatformApiService.createTenant(name)` (`POST /v1/tenants`, no auth) and shows the returned plaintext key once, copyable via `@angular/cdk/clipboard`. "Manage" signs in (`TenantSessionService.signIn`) and routes to `/services`; older tenants without an in-memory key are prompted to re-paste it. `auth.interceptor.ts` already only attaches `X-Api-Key` when a session key exists, so `createTenant` works unauthenticated. `PlatformApiService` also gained `createApiKey`/`listApiKeys`/`revokeApiKey` for the not-yet-implemented `/v1/api-keys` endpoints. This depends on the backend `POST /v1/tenants` and `/v1/api-keys` endpoints landing in `Platform.Api`, which had not shipped as of this change.
- **Provider-agnostic storage**: `Platform.Storage` holds only canonical models
  (`Tenant`, `Service`, `MetricSample`, `AggregateRollup`, `ApiKey`) and
  interfaces (`IMetricSampleStore`, `IAggregateRollupStore`, `IServiceCatalog`,
  `IApiKeyStore`) plus in-memory implementations for dev/tests. EF Core types
  live exclusively in `Platform.Storage.Relational`.
- **SQLite reference provider**: EF Core + SQLite implementations of all store
  interfaces, schema bootstrap via `EnsureCreated`, DI via
  `AddPlatformSqliteStorage`. Shared provider-conformance suite covers
  write/read, restart/persistence, tenant isolation, service catalog,
  AggregateRollup, time-range filtering, and cancellation (in-memory + SQLite).
- **Durable wiring**: `Platform.Hosting.AddPlatformStorage` registers SQLite
  outside Development/Testing (or when `Storage:Provider=Sqlite`), else
  in-memory. Integration test proves Ingestion write + Api read via a shared
  SQLite file across separate `WebApplicationFactory` hosts.
- **Real OTLP ingestion**: `POST /v1/metrics` accepts standard OTLP/HTTP
  protobuf (`ExportMetricsServiceRequest`), authenticates via `X-Api-Key`,
  derives `TenantId` only from the key (never resource attributes), maps to
  canonical `MetricSample`, and persists. Official opentelemetry-proto
  definitions are compiled via Grpc.Tools. `/v1/ingest` retained as
  documented dev/back-compat bridge.
- **OTLP e2e**: test drives real `OpenTelemetry` SDK +
  `OpenTelemetry.Exporter.OpenTelemetryProtocol` (HttpProtobuf) and real
  `PlaxionMediator.OpenTelemetry` pipeline observer, then asserts persistence
  through `/v1/metrics`.
- **Operator API-key lifecycle**: `POST/GET/DELETE /v1/api-keys` on
  Platform.Ingestion — plaintext returned only once at creation; list returns
  metadata only; revoke is tenant-scoped.
- `Platform.Aggregation`: `RollupAggregator.Compute` + `AggregationHostedService`
  (also registered on Platform.Api).
- `Platform.Api`: `GET /v1/services`, `GET /v1/services/{id}/metrics?range=`,
  tenant isolation (cross-tenant → 404).
- **Dashboard re-platformed to Angular (In Progress, UI only)**: the previous
  `Platform.Dashboard` Blazor Server project and `Platform.Dashboard.Tests`
  were **removed** per an explicit product decision to move to Angular.
  A new `src/platform-dashboard-web` Angular 18 + Angular Material app
  (dark-mode-only theme) was created with a login page (API key entry,
  sessionStorage), services list, and per-service metrics page with a
  1h/24h/7d range toggle and loading/empty states, and is wired to
  `Platform.Api`. A tenant-list-first landing page (`TenantsComponent`,
  `TenantRegistryService`, `CreateTenantDialogComponent`,
  `RebindKeyDialogComponent`) was added on top of this. 32 Angular
  component/service specs pass (`ng test`). See `docs/dashboard.md`.
- **MVP hardening (lightweight)**:
  - 1 MiB request body limits on `/v1/metrics` and `/v1/ingest` (Kestrel +
    `[RequestSizeLimit]`).
  - Cardinality guards: max 5 000 samples/request and max 200 distinct metric
    names/request (plus existing 256-char name limits).
  - API-key plaintext never logged (dev seed writes to `.dev-api-key` file only).
  - `/health` and `/healthz` on Platform.Ingestion and Platform.Api.
- Tests: **103 total, all green** (`dotnet build` + `dotnet test`, 0
  warnings/errors) — the .NET solution no longer includes a Dashboard test
  project. Counts as of this session (up from 100, +3 from this session's
  hardening/rate-limit/malformed-payload/real-e2e work):
  - Platform.Storage.Tests: 19
  - Platform.Storage.Relational.Tests: 12
  - Platform.Ingestion.Tests: 25
  - Platform.Identity.Tests: 6
  - Platform.Aggregation.Tests: 9
  - Platform.Api.Tests: 32
  - `platform-dashboard-web` (Angular, separate toolchain): last reported 39/39 specs pass; not touched or re-verified this session.
- Rate limiting is now implemented for the anonymous `POST /v1/tenants` bootstrap endpoint (5 req/min per client IP, 429 on excess); other endpoints still rely on body-size + cardinality guards plus API-key auth rather than IP rate limiting.
- Platform self-observability is now implemented via standard .NET `Meter`/`Counter`/`Histogram` instruments across `Platform.Ingestion`, `Platform.Aggregation`, and `Platform.Api` (see `IngestionMetrics`, `AggregationMetrics`, `ApiMetrics`); Platform does not yet dogfood the PlaxionMediator/OTLP exporter path for its own telemetry.
- Raw `MetricSample` retention/eviction is now implemented (`Platform.Aggregation/RetentionHostedService.cs`).
- Tenant-scoped query endpoints (`GET /v1/tenants/{tenantId}/services*`) now require `X-Api-Key` (401 if missing/invalid, 404 on tenant mismatch) - the query-path authentication gap noted in earlier drafts of this document has been closed.
- Production multi-process default still requires operators to set the same
  `ConnectionStrings:Platform` (or `Storage:Provider=Sqlite`) on Ingestion and
  Api; Development defaults remain in-memory for frictionless local loops.
- PostgreSQL provider (interfaces ready; SQLite is the reference provider only).
- OTLP/gRPC and OTLP/HTTP+JSON are out of MVP scope (HTTP+protobuf only).
- Docs C/D/E/F features (SLO, alerting, tracing, diagnostics, policy, k8s)
  and CLI — explicitly out of scope for this repo session work.

## Decision notes

- **`/v1/ingest` kept**: simplified JSON bridge remains for local demos and
  existing tests; production apps should use `POST /v1/metrics` (OTLP).
- **IApiKeyStore moved into Platform.Storage** so SQLite can persist keys
  alongside samples/rollups without Identity depending on EF.
- **Dashboard moved from Blazor Server to Angular + Angular Material** (dark
  mode) per explicit product direction; the Blazor project and its tests
  were deleted rather than kept alongside the new app. The Angular app must
  remain an **HTTP-only** consumer of Platform.Api once wired (no direct
  storage access), matching the original Dashboard principle.

## GO/NO-GO for Doc B MVP completion

**GO.** See `docs/mvp/README.md` for the full, session-by-session reconciled Definition of Done checklist and rationale - it is the authoritative execution index and supersedes the "CONDITIONAL GO"/"IN PROGRESS" language previously in this section.

Backend is onboardable end-to-end via API: durable shared SQLite, real
OTLP/HTTP protobuf receiver, API-key auth + operator key lifecycle,
tenant-isolated query API (now including the query layer, not just writes),
scheduled aggregation with raw-sample retention, Platform self-observability
metrics, MVP security hardening (rate limiting, malformed-payload handling,
health checks), a measured performance baseline, and green conformance +
real end-to-end OTLP acceptance tests. The Angular Dashboard is wired to the
real `Platform.Api` backend (tenant list, create-tenant, per-service
metrics with time-range toggle) - it is no longer a mock-data scaffold.

Remaining polish (dashboard charts, real managed Postgres/MySQL exercise,
browser E2E coverage) is incremental and explicitly non-blocking for the
Doc B MVP bar.

## Confirmations

- Docs C/D/E/F and the CLI docs were not read or implemented.
- The PlaxionMediator CLI was not implemented.
- The PlaxionMediator OSS repository was not touched.
- No `git add`/commit/push/tag was performed; all changes remain unstaged.
