# Doc B MVP — Execution Index

**STATUS: GO FOR CURRENT PLATFORM MVP, reconciled 2026-08-26.** All Doc B
release-blocking Definition of Done items remain implemented and verified,
**and the MVP has since grown beyond Doc B's original scope**: generic OIDC
human authentication, `TenantMembership` (Owner/Member), a full invitation
lifecycle, and `PlatformAdministrator` break-glass authorization are now
implemented and tested — none of this existed when this index was last
written, and earlier drafts of this document (and of `docs/mvp-status.md`,
`docs/architecture-overview.md`, `docs/mvp-limitations-and-exclusions.md`)
described a pre-OIDC, anonymous-tenant-creation product that no longer
reflects reality. This reconciliation pass also closed a real coverage gap:
the 12 `PostgreSqlConformanceTests` had never been executed against a live
PostgreSQL server anywhere on record; they now run in CI against a real
`postgres:16-alpine` service container and pass (232/232 backend tests
total, 0 skipped).

The frozen, authoritative statement of current MVP scope (supported / not
supported / self-hosted preview / future) is `platform-mvp-contract.md` in
the documentation repository — this index describes implementation
detail and history; that document is authoritative on scope.

This document is the execution index for the Platform product. It reflects
the repository as verified in code/tests today, not just prior status docs.
Where prior docs disagreed with the actual code, this document corrects them
and calls out the discrepancy explicitly, per this repo's own stated
practice of not letting status documents drift from source.

---

### 1. Where are we today?

#### Current MVP architecture summary

```
Customer app (any standard OpenTelemetry SDK, OTLP/HTTP-protobuf exporter)
        v
Platform.Ingestion  -- POST /v1/metrics (OTLP/HTTP+protobuf, X-Api-Key auth)
                    -- POST /v1/ingest  (legacy/dev JSON bridge, X-Api-Key auth)
                    -- /v1/api-keys (dual API-key/JWT-Owner auth)
        |
        v
Platform.Storage (canonical models + provider-neutral interfaces)
        |
        +-- in-memory provider (tests / Development default)
        +-- Platform.Storage.Relational (EF Core: Sqlite / PostgreSql fully migrated + conformance-tested;
        |                                MySql present, EnsureCreated-only, not chart-supported)
        ^
        |
Platform.Aggregation (scheduled background rollups: throughput, P50/P95/P99, failure rate)
        ^
        |
Platform.Api  -- POST /v1/tenants (creation-policy-gated, rate-limited)
              -- /v1/tenants/{id}/memberships (PlatformAdministrator-only)
              -- /v1/invitations (create/list/revoke, Owner + ManageInvitations permission)
              -- /v1/api-keys (dual API-key/JWT-Owner auth)
              -- GET /v1/tenants/{tenantId}/services            (OIDC JWT; 401/404 enforced)
              -- GET /v1/tenants/{tenantId}/services/{id}/metrics (OIDC JWT; 401/404 enforced)
              -- GET /health, /health/ready
        ^
        |  OIDC Bearer JWT (human) or X-Api-Key (legacy anonymous mode only)
        |
platform-dashboard-web (Angular 18 + Material, OIDC-first, dark theme)
```

`Platform.Identity` is a shared library (API-key hashing/auth handler/
claims) providing **telemetry-ingestion authentication only** — not a
standalone runtime service, and not the mechanism behind human sign-in,
which is OIDC wired directly in `Platform.Api`/`Platform.Ingestion`.

#### Current end-to-end flow (verified against code)

1. An Owner signs into the Angular dashboard (`/`) via OIDC (Authorization
   Code + PKCE); `TenantsComponent` calls `GET /v1/tenants` under the
   resulting JWT, which just-in-time-claims any pending invitations for that
   user's email before returning the list.
2. The Owner clicks **Create Tenant**, which calls `POST /v1/tenants`
   (subject to the configured tenant-creation policy) and receives a
   `TenantId` plus a one-time plaintext telemetry API key. The creator
   automatically becomes that tenant's Owner.
3. The Owner copies the key into an instrumented app (standard OTel SDK +
   OTLP/HTTP exporter, `X-Api-Key` header); the app sends OTLP metrics to
   `POST /v1/metrics` on `Platform.Ingestion`.
4. `Platform.Ingestion` authenticates the key, derives `TenantId`
   server-side, maps OTLP to the canonical `MetricSample`, and persists it.
5. `Platform.Aggregation` periodically reads samples and writes
   `AggregateRollup`s (throughput, P50/P95/P99 latency, failure rate), then
   evicts raw samples past the retention window.
6. The Owner can invite others (`POST /v1/invitations`, batch, scoped to
   tenants they own); invitees are told out-of-band to sign in with the
   invited email address — there is no email delivery. On their next
   `GET /v1/tenants` call, the invited user is granted a `Member`
   `TenantMembership` automatically.
7. Any tenant member navigates to `/tenants/{tenantId}/services`
   (route-guarded, requires a valid OIDC token); the dashboard calls
   `GET /v1/tenants/{tenantId}/services` and `.../metrics` under the JWT and
   renders charts/table with a time-range toggle (1h/24h/7d). A mismatch
   between the token's tenant memberships and the URL's `tenantId` returns
   404, never another tenant's data.
8. A legacy anonymous path (`authentication.enabled=false`) still exists for
   throwaway/eval deployments: the dashboard falls back to a
   `localStorage`-backed tenant registry and per-tenant API-key session
   instead of OIDC. Production refuses to start this way unless
   `AllowAnonymousProduction=true` is explicitly set.

This is a real, working, verified happy path — both the OIDC path (the
current default/recommended mode) and the legacy anonymous path (retained
for evaluation only).

---

### 2. What exactly is missing?

#### Completed capabilities (DONE — verified in code/tests)

| Capability | Evidence |
|---|---|
| Canonical `MetricSample`/`AggregateRollup`/`Tenant`/`TenantMembership`/`TenantInvitation` model | `Platform.Storage/CanonicalModels.cs`, `Platform.Storage.Tests` |
| Provider-neutral storage contracts | `Platform.Storage/*.cs` — no EF Core/SQL leakage into contracts |
| In-memory, SQLite, PostgreSQL storage providers, EF-migrated and conformance-tested (PostgreSQL now against a **live server**, in CI) | `Platform.Storage.Relational`, `RelationalStorageConformanceTests`, `PostgreSqlConformanceTests` (12/12 against real Postgres), `.github/workflows/ci.yml` |
| MySQL storage provider (partial — `EnsureCreated` only, not chart-supported) | `PlatformStorageRegistration.cs` |
| Real OTLP/HTTP+protobuf ingestion endpoint | `Platform.Ingestion/Controllers/OtlpMetricsController.cs` |
| Telemetry API-key authentication (hash+compare, tenant claim) | `Platform.Identity/*` |
| Generic OIDC human authentication (Authorization Code + PKCE, provider-neutral discovery) | `Platform.Api/Program.cs`, `TenantOidcAuthorizationTests` |
| `TenantMembership` (Owner/Member), tenant-local authorization cleanly separated from tenant-creation policy | `TenantAuthorizationService`, `TenantMembershipsController` |
| Invitation lifecycle (create/list/revoke, JIT claim, duplicate protection) | `InvitationsController`, `TenantInvitationsTests` |
| `PlatformAdministrator` break-glass, default-safe | `PlatformAdministratorAuthorizationHandler` |
| Self-service tenant + first API key bootstrap, rate-limited | `TenantsController`, `TenantsEndpointTests` |
| Operator API-key lifecycle, dual API-key/JWT-Owner auth | `ApiKeysController` (both apps), `ApiKeysEndpointTests` |
| Scheduled aggregation (throughput, P50/P95/P99, failure rate) + raw-sample retention | `Platform.Aggregation`, `RetentionHostedService` |
| Angular dashboard: OIDC-first login, tenant/service/metrics/invitations UI, real charts, paginated table | `platform-dashboard-web/src/app/*` |
| Production migrations (SQLite/PostgreSQL) + fail-fast guards | `RelationalMigrationRunner`, `DeploymentReadinessTests` |
| Health/readiness, matched by Helm chart probe paths | `/health`, `/health/ready` |
| Single multi-role container image + official Helm chart, 8 example deployment profiles | `Dockerfile`, `plaxion-platform` repo |

#### Partial capabilities (PARTIAL)

| Capability | Gap |
|---|---|
| MySQL storage provider | No EF Core migrations assembly (`EnsureCreated` only), no dedicated conformance tests, not exposed by the Helm chart's `database.provider` enum (`PostgreSql` only). |
| OIDC against a specific real identity provider | Verified generically (protocol-level, synthetic JWT); Keycloak/Duende have worked-example docs and Helm values but no automated live-instance integration test. |
| Dashboard API-key management / member removal | Both underlying APIs exist; no dedicated UI screen for either. |
| Helm OIDC example profiles in CI | 4 of 8 example profiles render correctly under manual `helm template` but are not in the chart's own CI matrix. |

#### Not started

- Angular end-to-end (Cypress/Playwright) coverage of the live flow against
  a running backend (unit/component tests exist; no browser-driven E2E
  harness).
- Invitation email delivery.
- Live-instance automated integration tests against a specific real OIDC
  provider.

#### Resolved since the prior draft of this index

- **OIDC authentication, tenant memberships, and invitations** — added
  since Doc B's original scope, fully implemented and tested, and now
  accurately documented here (previously this index, `mvp-status.md`,
  `architecture-overview.md`, and `mvp-limitations-and-exclusions.md` all
  described a pre-OIDC, anonymous-only product).
- **PostgreSQL conformance evidence** — the 12 `PostgreSqlConformanceTests`
  had never been executed against a live server anywhere on record (they
  auto-skip without a connection string, and CI provisioned no PostgreSQL
  service container). They now run in CI against a real
  `postgres:16-alpine` service container and pass.
- Dashboard time-series charts and server-side paginated request-types
  table (tracked as gaps in older drafts) were already resolved by the time
  of this reconciliation.
- Runaway `aggregate_rollups` growth and SQLite reader/writer contention
  (production bugs found via manual testing in an earlier session) were
  already fixed via upsert-by-key and WAL mode respectively.

#### Blocked

- None identified that require external credentials or infrastructure.

#### Deviated from plan (explicit, intentional)

1. **Query-path authentication requires OIDC (or, in legacy anonymous mode,
   an API key)** — not left unauthenticated. `ServicesController`/
   `ServiceMetricsController` require a valid tenant membership; a mismatch
   returns 404, never another tenant's data.
2. **Dashboard technology is Angular 18 + Material**, not the earlier
   Blazor Server prototype (removed entirely, per an explicit prior product
   decision retained here for historical accuracy — this is no longer a
   live consideration).
3. **`/v1/ingest` simplified JSON transport kept** as a documented
   dev/back-compat bridge alongside the real OTLP endpoint.
4. **OIDC human authentication and the invitation system were added after**
   the original Doc B scope was drafted — a deliberate scope extension, not
   a deviation from a still-current plan.

#### Out-of-scope confirmation

Docs C (Observability & Analytics), D (Diagnostics), E (Policy &
Governance), F (Cloud/Kubernetes application patterns beyond the Helm
chart) and the CLI: no code under this repository implements any of them.
No OSS `PlaxionMediator` repository file was modified by this
reconciliation; Platform only consumes released NuGet packages.

---

### 3. Known gaps, technical debt, and security/testing/documentation gaps

**Technical debt**

- `ApiKeysController` exists in two places (`Platform.Ingestion` and
  `Platform.Api`) with ~70% duplicated Create/List/Revoke logic and no
  shared base class — acceptable MVP debt, worth consolidating (P2).
- MySQL lacks an EF Core migrations assembly — either add one before
  offering MySQL as supported, or keep it explicitly unsupported (it
  already is, at the Helm chart level) (P2).

**Security gaps (release-relevant)**

- No BLOCKER/HIGH findings from source review. Tenant-scoped queries
  require OIDC or API-key auth (401/404 enforced); `POST /v1/tenants` is
  rate-limited; malformed OTLP payloads return 400 not 500; no plaintext
  API-key logging found in `Platform.Identity`/`Platform.Ingestion`/
  `Platform.Api`.
- `PlatformAdministrator` break-glass defaults to `Disabled` — an
  operational/config risk if enabled in production, not a code defect.
- Remaining, non-blocking: no formal secrets-scanning/log-redaction
  automation (manual grep-based audit only); no WAF/DDoS-layer protection
  (expected to sit in front of the service in a real deployment).

**Testing gaps**

- Resolved this reconciliation: PostgreSQL conformance tests now execute
  against a live server in CI (previously a real, undocumented gap).
- Remaining: no Angular end-to-end (Cypress/Playwright) browser-driven
  coverage against a live backend; no automated integration test against a
  specific real OIDC identity provider.

**Documentation gaps**

- This document, `docs/mvp-status.md`, `docs/architecture-overview.md`, and
  `docs/mvp-limitations-and-exclusions.md` were reconciled against verified
  current code as of 2026-08-26 — re-verify periodically as
  controllers'/routes'/auth attributes evolve. The frozen scope contract,
  `platform-mvp-contract.md`, is authoritative on what is in/out of scope;
  these four documents are authoritative on implementation detail and must
  not contradict it.

---

### 4. Recommended next work

All P0/P1 items tracked against this MVP are closed as of this
reconciliation. Remaining P2/P3 items (see `docs/mvp-limitations-and-exclusions.md`
and the checkpoint referenced by `platform-mvp-contract.md`) are non-blocking
polish: consolidating the duplicated `ApiKeysController` logic, an EF Core
migrations assembly for MySQL (or formally dropping MySQL support), adding
the 4 OIDC Helm example profiles to the chart's CI matrix, a standalone
Dashboard API-key management page, a "remove member" UI action, and
Cypress/Playwright E2E coverage.

The next strategic milestone beyond MVP polish — per-behavior/handler-level
"semantic mediator observability" — is explicitly **not** part of this MVP
and was not started by this reconciliation. See `platform-mvp-contract.md`
§4 (FUTURE / V2) for the full boundary.

---

### 5. Verification summary

- `dotnet build PlaxionMediator.Platform.sln -c Release`: 0 warnings, 0
  errors.
- `dotnet test` against a live PostgreSQL 16 instance: **232/232 passing, 0
  skipped** (Platform.Storage.Tests 26, Platform.Storage.Relational.Tests
  27 [15 in-memory/SQLite + 12 live PostgreSQL], Platform.Ingestion.Tests
  45, Platform.Identity.Tests 6, Platform.Aggregation.Tests 11,
  Platform.Api.Tests 117).
- Angular (`ng test --browsers=ChromeHeadless`): **122/122 passing**.
- Angular production build: succeeds (non-fatal bundle-size budget warnings
  only).
- `helm lint` (plaxion-platform chart): 0 charts failed.
- `helm template`, all 8 example profiles: 8/8 rendered successfully.
- `docker build .`: succeeds; runtime smoke test (`ROLE=api`) started
  cleanly and returned `200 Healthy` from `GET /health`.

---

### GO FOR CURRENT PLATFORM MVP

Scope, evidence, and remaining gaps are recorded in `platform-mvp-contract.md`
(documentation repository) and `docs/mvp-limitations-and-exclusions.md`
(this repository). No release-blocking item remains open.
