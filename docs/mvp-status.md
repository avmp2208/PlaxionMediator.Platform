# MVP Status — honest snapshot

This document is the authoritative, continuously-updated record of what is
real vs. what remains for the Platform product. It is kept in sync with
`docs/architecture-overview.md`, `docs/mvp-limitations-and-exclusions.md`,
and `docs/mvp/README.md` — if any of these disagree, treat the actual code
and tests as ground truth and fix the stale document. The frozen scope
contract for the current MVP lives in the documentation repository:
`platform-mvp-contract.md`.

**Last reconciled against source: 2026-08-26** (full backend test suite
re-run against a live PostgreSQL 16 instance: 232/232 passing, 0 skipped;
Angular: 122/122 passing).

## Component status at a glance

| Component | Status |
|---|---|
| `Platform.Storage` / `Platform.Storage.Relational` | Done (In-memory, SQLite, PostgreSQL all conformance-tested; MySQL partial, see limitations) |
| `Platform.Hosting` | Done |
| `Platform.Identity` | Done — API-key hashing/handler for **telemetry ingestion auth** |
| `Platform.Ingestion` (OTLP + JSON bridge + API-key admin) | Done |
| `Platform.Aggregation` | Done |
| `Platform.Api` | Done — tenants, memberships, invitations, API keys, services/metrics, OIDC |
| `platform-dashboard-web` (Angular Dashboard) | Done — OIDC-first, tenant/service/metrics/invitations UI |

## Two separate authentication mechanisms — do not conflate them

This is the single most important correction to earlier drafts of this
document: **Platform has two independent authentication paths for two
different audiences**, and neither replaced the other.

1. **Telemetry ingestion (machine-to-machine)** — unchanged since the
   original MVP. Every `POST /v1/metrics` / `POST /v1/ingest` call
   authenticates via `X-Api-Key`; `TenantId` is derived only from the
   authenticated key, never from OTLP resource attributes or the URL.
   `Platform.Identity`'s `ApiKeyAuthenticationHandler` implements this.
2. **Human sign-in to the Dashboard/API (added since the original MVP
   draft of this document)** — generic **OIDC** (Authorization Code + PKCE),
   provider-neutral (`Authentication:Authority`/`Authentication:Audience`
   drive standard `.well-known/openid-configuration` + JWKS discovery, no
   provider-specific code). This is what changed since earlier drafts of
   this document described a "tenant-list-first, localStorage-only" flow —
   that flow is now the **legacy fallback**, active only when
   `authentication.enabled=false`.

`GET/POST /v1/api-keys` accepts **either** a valid API key or a JWT with the
tenant's Owner role — operators can manage telemetry keys either way.

## Done (implemented and test-verified)

- Solution bootstrap: `PlaxionMediator.Platform.sln`, `Directory.Build.props`,
  `docs/`, and projects: `src/Platform.Ingestion`, `src/Platform.Storage`,
  `src/Platform.Storage.Relational`, `src/Platform.Hosting`,
  `src/Platform.Identity`, `src/Platform.Aggregation`, `src/Platform.Api`,
  with matching `tests/*.Tests` projects.
- **Real OTLP ingestion**: `POST /v1/metrics` accepts standard OTLP/HTTP
  protobuf (`ExportMetricsServiceRequest`), authenticates via `X-Api-Key`,
  derives `TenantId` only from the key, maps to canonical `MetricSample`,
  and persists. `/v1/ingest` retained as documented dev/back-compat bridge.
- **Provider-agnostic storage**: `Platform.Storage` holds canonical models
  (`Tenant`, `Service`, `MetricSample`, `AggregateRollup`, `ApiKey`,
  `TenantMembership`, `TenantInvitation`) and interfaces, plus in-memory
  implementations. EF Core types live exclusively in
  `Platform.Storage.Relational`.
- **SQLite and PostgreSQL providers**: EF Core + versioned migrations for
  both. Shared provider-conformance suite covers write/read,
  restart/persistence, tenant isolation, service catalog, `AggregateRollup`
  upsert-dedup, time-range filtering, retention/`DeleteOlderThanAsync`, and
  cancellation. As of this reconciliation, the 12 PostgreSQL conformance
  tests were executed against a live PostgreSQL 16 server (not just SQLite)
  and pass; this now runs in CI on every build via a `postgres:16-alpine`
  service container (`.github/workflows/ci.yml`).
- **MySQL provider**: implemented at the storage-interface level but
  `EnsureCreated`-only (no EF Core migrations assembly) and has no dedicated
  conformance-test coverage. **Not offered by the Helm chart.** Treat as
  present-in-code, not production-supported.
- **OIDC human authentication**: `Program.cs` wires
  `Authentication:Authority`/`Authentication:Audience` to standard OIDC
  discovery + JWKS validation, `MapInboundClaims=false` preserves short
  claim names. Verified via a synthetic in-test JWT scheme
  (`TenantOidcAuthorizationTests`); not yet exercised against a live
  Keycloak/Duende/other IdP process in this repository's own test suite
  (Keycloak/Duende have worked-example docs and Helm values instead — see
  `docs/deployment/authentication.md`).
- **TenantMembership (Owner/Member)**: coarse, intentionally-not-full-RBAC
  two-tier model (`TenantAuthorizationService.RoleGrants`) — Owner gets
  `ViewTenant` + `ManageInvitations` + `ManageApiKeys`; Member gets
  `ViewTenant` only. Cross-tenant access returns 404 (not 403), specifically
  to avoid tenant-ID enumeration
  (`TenantMembershipAuthorizationResultHandler`).
- **Invitation lifecycle**: create (multi-tenant batch, Owner-only,
  all-or-nothing pre-check across requested tenants), list, revoke
  (`Pending → Revoked` only), duplicate-pending protection (app check + DB
  unique constraint → 409), just-in-time claim on the caller's next
  `GET /v1/tenants` call keyed off the OIDC email claim. **No email is
  sent** — invitees must be told out-of-band. See
  `docs/tenant-invitations-and-roles.md` for the full model.
- **PlatformAdministrator break-glass**: default mode `Disabled` (safe
  default confirmed in code, not just config docs); `Role`/`Claim` modes
  grant cross-tenant membership power when explicitly enabled.
- **Angular Dashboard**: OIDC-first (Authorization Code + PKCE via
  `angular-oauth2-oidc`, tokens in `sessionStorage`, automatic silent
  refresh). `/` (`TenantsComponent`) is still the landing route, but now
  calls `GET /v1/tenants` under a valid access token when OIDC is enabled;
  the earlier `localStorage`-only, unauthenticated tenant-list flow survives
  only as the fallback when `authentication.enabled=false` (`auth.guard.ts`
  routes accordingly). Tenant-local UI capability gating
  (`tenant-capabilities.ts`) reads **only** the server-returned
  `TenantMembership.role`, never OIDC/IdP claims. Throughput/P50/P95/P99
  latency/failure-rate time-series charts (`ng2-charts`/Chart.js) with
  correct idle/gap semantics, server-side paginated and server-ordered
  request-types table. Invitations UI (create/list/revoke) at `/invitations`
  (guarded route). No standalone API-key management page and no "remove
  existing member" action exist yet — both underlying APIs exist without a
  dedicated screen.
- `Platform.Aggregation`: `RollupAggregator.Compute` +
  `AggregationHostedService` (scheduled), plus `RetentionHostedService` for
  raw-sample eviction.
- `Platform.Api`: tenants (CRUD + configurable creation policy), tenant
  memberships, invitations, API keys (dual API-key/JWT-Owner auth),
  `GET /v1/tenants/{tenantId}/services`,
  `GET /v1/tenants/{tenantId}/services/{id}/metrics`, health/readiness.
- **MVP hardening (lightweight)**:
  - 1 MiB request body limits on `/v1/metrics` and `/v1/ingest` (Kestrel +
    `[RequestSizeLimit]`).
  - Cardinality guards: max 5,000 samples/request and max 200 distinct
    metric names/request (plus 256-char name limits).
  - API-key plaintext never logged; shown once at creation only.
  - `/health`, `/healthz` (liveness), `/health/ready` (DB-aware readiness)
    on both `Platform.Ingestion` and `Platform.Api`.
  - Production fail-fast guards confirmed in code: refuses to start with
    `Authentication:Enabled=false` in Production unless
    `AllowAnonymousProduction=true` is explicit; refuses `InMemory`/`Sqlite`
    storage in Production/Staging/QA.
- **Tests, current count**: **232 backend tests, all green, 0 skipped**
  (`dotnet build` + `dotnet test` against a live PostgreSQL instance, 0
  warnings/errors):
  - Platform.Storage.Tests: 26
  - Platform.Storage.Relational.Tests: 27 (15 in-memory/SQLite + **12
    PostgreSQL conformance, executed against a real server**)
  - Platform.Ingestion.Tests: 45
  - Platform.Identity.Tests: 6
  - Platform.Aggregation.Tests: 11
  - Platform.Api.Tests: 117 (includes OIDC/tenant-membership/invitation
    coverage)
  - `platform-dashboard-web` (Angular, separate toolchain): **122/122**
    specs pass (`ng test --browsers=ChromeHeadless`).
- Rate limiting implemented for the anonymous `POST /v1/tenants` bootstrap
  endpoint (5 req/min per client IP, 429 on excess); other endpoints rely on
  body-size + cardinality guards plus API-key/OIDC auth rather than IP rate
  limiting.
- Platform self-observability via standard .NET `Meter`/`Counter`/
  `Histogram` instruments across `Platform.Ingestion`, `Platform.Aggregation`,
  and `Platform.Api`; Platform does not yet dogfood the
  PlaxionMediator/OTLP exporter path for its own telemetry.
- OTLP/gRPC and OTLP/HTTP+JSON are out of MVP scope (HTTP+protobuf only).
- Docs C/D/E/F features (SLO, alerting, tracing, diagnostics, policy, k8s
  application patterns beyond the Helm chart itself) and the CLI —
  explicitly out of scope; see `platform-mvp-contract.md` for the current
  future-boundary list.

## Decision notes

- **`/v1/ingest` kept**: simplified JSON bridge remains for local demos and
  existing tests; production apps should use `POST /v1/metrics` (OTLP).
- **`IApiKeyStore` lives in `Platform.Storage`** so SQLite/PostgreSQL can
  persist keys alongside samples/rollups without `Platform.Identity`
  depending on EF.
- **Dashboard is Angular + Angular Material** (dark mode), an HTTP-only
  consumer of `Platform.Api`.
- **OIDC was added after the original Doc B MVP scope was drafted.** Earlier
  drafts of this document explicitly listed SSO/OAuth as out of scope; that
  is no longer true and has been corrected throughout this document and
  `docs/mvp-limitations-and-exclusions.md`.

## Current MVP scope authority

This document describes implementation status. For the frozen statement of
*what is in vs. out of scope* for the current MVP (including the explicit
FUTURE/V2 boundary — trace ingestion, per-behavior/handler timing, SLOs,
alerting, policy/governance, full RBAC, invitation email delivery, etc.),
see `platform-mvp-contract.md` in the documentation repository. That
document, not this one, is authoritative on scope; this document is
authoritative on implementation/test status.

## Confirmations

- Docs C/D/E/F and the CLI docs were not read or implemented as part of the
  MVP-closure pass that produced this reconciliation.
- The PlaxionMediator CLI was not implemented.
- The PlaxionMediator OSS repository was not touched by this reconciliation.
- No new product capability (tracing, SLOs, alerting, billing, licensing)
  was added by this reconciliation pass — it corrected documentation and
  closed a CI/testing evidence gap (live PostgreSQL conformance) only.
