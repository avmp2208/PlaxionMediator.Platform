# MVP Status - honest snapshot

This project is being built under a constrained, multi-session step budget
against the very large Doc B execution plan. This document is the
authoritative, continuously-updated record of what is real vs. what
remains. See also `docs/storage-decision.md` and `docs/nuget-reconciliation.md`.

## Component status at a glance

| Component | Status |
|---|---|
| `Platform.Storage` / `Platform.Storage.Sqlite` | Done |
| `Platform.Hosting` | Done |
| `Platform.Identity` | Done |
| `Platform.Ingestion` (OTLP + JSON bridge + API-key admin) | Done |
| `Platform.Aggregation` | Done |
| `Platform.Api` | Done |
| `platform-dashboard-web` (Angular Dashboard) | Done |

## Done (implemented and test-verified)

- Solution bootstrap: `PlaxionMediator.Platform.sln`, `Directory.Build.props`,
  `docs/`, and projects:
  `src/Platform.Ingestion`, `src/Platform.Storage`, `src/Platform.Storage.Sqlite`,
  `src/Platform.Hosting`, `src/Platform.Identity`, `src/Platform.Aggregation`,
  `src/Platform.Api`, with matching `tests/*.Tests` projects.
- **Angular Dashboard**: Wired to `Platform.Api` (`/v1/services`, `/v1/services/{id}/metrics`) using Angular 18 functional interceptors for `X-Api-Key` auth. Environment-based configuration supports local, staging, qa, and production. Dark-mode Material theme, login validation, and loading/error handling implemented.
- **Provider-agnostic storage**: `Platform.Storage` holds only canonical models
  (`Tenant`, `Service`, `MetricSample`, `AggregateRollup`, `ApiKey`) and
  interfaces (`IMetricSampleStore`, `IAggregateRollupStore`, `IServiceCatalog`,
  `IApiKeyStore`) plus in-memory implementations for dev/tests. EF Core types
  live exclusively in `Platform.Storage.Sqlite`.
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
  1h/24h/7d range toggle and loading/empty states — all rendering against an
  in-memory `MockDataService`. **It is not yet wired to `Platform.Api`.**
  7 Angular component specs pass (`ng test`). See `docs/dashboard.md`.
- **MVP hardening (lightweight)**:
  - 1 MiB request body limits on `/v1/metrics` and `/v1/ingest` (Kestrel +
    `[RequestSizeLimit]`).
  - Cardinality guards: max 5 000 samples/request and max 200 distinct metric
    names/request (plus existing 256-char name limits).
  - API-key plaintext never logged (dev seed writes to `.dev-api-key` file only).
  - `/health` and `/healthz` on Platform.Ingestion and Platform.Api.
- Tests: **60 total, all green** (`dotnet build` + `dotnet test`, 0
  warnings/errors) — the .NET solution no longer includes a Dashboard test
  project:
  - Platform.Storage.Tests: 14
  - Platform.Storage.Sqlite.Tests: 7
  - Platform.Ingestion.Tests: 22
  - Platform.Identity.Tests: 6
  - Platform.Aggregation.Tests: 4
  - Platform.Api.Tests: 7
  - `platform-dashboard-web` (Angular, separate toolchain): wired to real HTTP, 7 specs pass.
- Rate limiting (beyond body-size + cardinality guards).
- Self-observability dogfooding (Platform emitting its own OTLP metrics).
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

**CONDITIONAL GO for the backend; Dashboard is IN PROGRESS (UI only).**

Backend is onboardable end-to-end via API: durable shared SQLite, real
OTLP/HTTP protobuf receiver, API-key auth + operator key lifecycle,
tenant-isolated query API, scheduled aggregation, and green conformance +
e2e tests. The operator Dashboard is being rebuilt in Angular per product
direction; the current build is a **dark-mode UI-only scaffold** on mock
data and cannot yet be used to view real tenant data — use the HTTP API
directly (`docs/api-contracts.md`) to verify ingested/aggregated data until
the Dashboard is wired to `Platform.Api` in a follow-up task.

Remaining polish (Dashboard wiring, charts, rate limits, dogfooding,
Postgres) is incremental; blocking Doc B backend gaps that were open
previously (no health, no ingest limits, key-in-logs) are closed for the
lightweight MVP bar.

## Confirmations

- Docs C/D/E/F and the CLI docs were not read or implemented.
- The PlaxionMediator CLI was not implemented.
- The PlaxionMediator OSS repository was not touched.
- No `git add`/commit/push/tag was performed; all changes remain unstaged.
