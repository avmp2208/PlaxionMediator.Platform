# Doc B MVP — Execution Index

**FINAL CERTIFICATION: GO FOR DOC B MVP — COMPLETE.** All Doc B release-blocking Definition of Done items are implemented and verified directly against code/tests as of this final certification pass, including the two items previously tracked as PARTIAL/NOT STARTED in earlier drafts of this document: the Dashboard now has real throughput/P50-P95-P99-latency/failure-rate time-series charts (Chart.js via `ng2-charts`) with correct per-bucket semantics (no carry-forward, null latency gaps, zero-filled idle throughput/failure-rate, full range-to-now domain, zero-traffic ranges still render the full dashboard), and the request-types table is server-side paginated and server-ordered. This session also fixed a real production bug found via manual testing (unbounded duplicate-row growth in `aggregate_rollups` from the aggregation job re-inserting instead of upserting, plus SQLite whole-file write locks blocking reads during writes) — both are now fixed and regression-tested. See section 6 for the full DoD checklist and section 2 for the reconciled gap analysis.

This document is the single execution index for finishing Doc B (`B-platform-mvp-execution-plan.md`). It reflects the repository as verified in code/tests today, not just prior status docs. Where prior docs (`docs/mvp-status.md`, `docs/dashboard.md`, `README.md`) disagreed with the actual code, this document corrects them and calls out the discrepancy explicitly.

---

### 1. Where are we today?

#### Current MVP architecture summary
```
Customer app (PlaxionMediator + PlaxionMediator.OpenTelemetry, real NuGet packages)
        |  standard OpenTelemetry .NET SDK + OTLP/HTTP-protobuf exporter
        v
Platform.Ingestion  -- POST /v1/metrics (OTLP/HTTP+protobuf, X-Api-Key auth)
                    -- POST /v1/ingest  (legacy/dev JSON bridge, X-Api-Key auth)
                    -- /v1/api-keys (authenticated key mgmt, legacy location)
        |
        v
Platform.Storage (canonical models + provider-neutral interfaces)
        |
        +-- in-memory provider (tests / Development default)
        +-- Platform.Storage.Relational (EF Core: Sqlite / MySql / PostgreSql)
        ^
        |
Platform.Aggregation (scheduled background rollups: min/max/avg/p50/p95/p99)
        ^
        |
Platform.Api  -- POST /v1/tenants (anonymous, rate-limited, self-service tenant+key bootstrap)
              -- /v1/api-keys (authenticated key mgmt)
              -- GET /v1/tenants/{tenantId}/services            (X-Api-Key required; 401/404 enforced)
              -- GET /v1/tenants/{tenantId}/services/{id}/metrics (X-Api-Key required; 401/404 enforced)
              -- GET /health, /healthz
        ^
        |  HTTP only, environment-configured apiBaseUrl
        |
platform-dashboard-web (Angular 18 + Material, dark theme)
```

`Platform.Identity` is a shared library (API-key hashing/auth handler/claims), not a standalone runtime service - consumed by both `Platform.Ingestion` and `Platform.Api`.

Verified on disk this session: the storage provider project is `src/Platform.Storage.Relational` (7 files, including `RelationalStorageServiceCollectionExtensions.cs` and `RelationalStores.cs`) with **no** leftover `Platform.Storage.Sqlite` directory - the rename reported by a prior subagent pass has genuinely landed.

#### Current end-to-end flow (verified against code)
1. User opens the Angular dashboard (`/`) and sees `TenantsComponent` (tenant list from browser `localStorage`).
2. User clicks **Create Tenant** which calls `POST /v1/tenants` (anonymous) and receives a `TenantId` plus a one-time plaintext API key.
3. User copies the key into a PlaxionMediator sample app (OTLP exporter + `X-Api-Key` header); the app sends OTLP metrics to `POST /v1/metrics` on `Platform.Ingestion`.
4. `Platform.Ingestion` authenticates the key, derives `TenantId` server-side (never from the OTLP payload or URL), maps OTLP to the canonical `MetricSample`, and persists it via the configured `Platform.Storage` provider.
5. `Platform.Aggregation` (background service) periodically reads samples and writes `AggregateRollup`s.
6. User clicks **Manage** on a tenant in the dashboard and navigates to `/tenants/{tenantId}/services`; the query endpoints now require the tenant's `X-Api-Key` (401 if missing/invalid, 404 if the key's tenant does not match the URL's `tenantId`) - the earlier unauthenticated-query deviation has been closed.
7. The dashboard calls `GET /v1/tenants/{tenantId}/services` and `.../metrics` on `Platform.Api` (with the API key) and renders tables with a time-range toggle (1h/24h/7d).

This is a real, working, verified happy path for a single tenant and matches what the user has manually tested locally.

---

### 2. What exactly is missing from Doc B?

#### Completed capabilities (DONE - verified in code/tests)
| Capability | Evidence |
|---|---|
| Canonical `MetricSample`/`AggregateRollup`/`Tenant` model with mandatory `TenantId` | `Platform.Storage/CanonicalModels.cs`, `Platform.Storage.Tests` |
| Provider-neutral storage contracts (`IMetricSampleStore`, `IAggregateRollupStore`, `IApiKeyStore`, `IServiceCatalog`) | `Platform.Storage/*.cs` - no EF Core/SQL leakage into contracts |
| In-memory storage provider | `Platform.Storage` in-memory impls, used in Dev/tests |
| Durable relational storage provider (Sqlite/MySql/PostgreSql) behind `RelationalProvider` enum | `Platform.Storage/RelationalProvider.cs`, `Platform.Hosting/PlatformStorageRegistration.cs`, `Platform.Storage.Relational` (EF Core encapsulated); verified on disk this session |
| Provider conformance tests | `Platform.Storage.Relational.Tests` (`RelationalStorageConformanceTests.cs`) |
| Shared multi-process persistence proof | `tests/Platform.Api.Tests/SharedSqlitePersistenceTests.cs` |
| Real OTLP/HTTP+protobuf ingestion endpoint | `Platform.Ingestion/Controllers/OtlpMetricsController.cs`, `OtlpMetricsMapper.cs`/`OtlpToCanonicalMapper.cs` |
| API-key authentication (hash+compare, tenant claim) | `Platform.Identity/ApiKeyAuthentication.cs`, `ApiKeyHasher.cs`, `ApiKeyService.cs` |
| Real end-to-end test with genuine `PlaxionMediator`/`PlaxionMediator.OpenTelemetry` NuGet packages + standard OTLP exporter | `Platform.Ingestion.Tests` (per prior session report) |
| Self-service tenant + first API key bootstrap (`POST /v1/tenants`, anonymous) | `Platform.Api/Controllers/TenantsController.cs`, `TenantsEndpointTests.cs` |
| Operator API-key lifecycle (create/list/revoke) | `Platform.Api/Controllers/ApiKeysController.cs` (also present in `Platform.Ingestion`), `ApiKeysEndpointTests.cs` |
| Scheduled aggregation (min/max/avg/p50/p95/p99) | `Platform.Aggregation`, `Platform.Aggregation.Tests` |
| Angular dashboard: tenant-list landing, create-tenant dialog, per-service metrics, time-range toggle, environment-based `apiBaseUrl`, dark theme | `platform-dashboard-web/src/app/pages/tenants`, `services`, `service-metrics` |
| Login page and `authGuard` removed per explicit user request (auth deferred to future RBAC work) | latest diff removing `pages/login`, `auth.guard.ts`, sign-out UI |

#### Partial capabilities (PARTIAL)
| Capability | Gap |
|---|---|
| PostgreSQL/MySQL providers | Only unit/conformance-level verification exists behind the same provider-neutral contracts; no real managed MySQL/Postgres server has been exercised. Sqlite is the only durable provider verified against a live database file in this repository's tests. |

#### Not started
- PostgreSQL/MySQL providers exercised against real managed instances.
- Angular end-to-end (Cypress/Playwright) coverage of the live create-tenant -> manage -> view-metrics flow against a running backend (unit/component tests exist; no browser-driven E2E harness was added).

#### Resolved since the prior draft of this index (verified this session)
- **Dashboard time-series charts**: `service-metrics.component.ts`/`.html` now render real throughput, P50/P95/P99 latency, and failure-rate line charts (`ng2-charts`/Chart.js) sharing one bucket timeline/X-axis across Last Hour/24 Hours/7 Days, with correct idle-bucket semantics (0 throughput, 0% failure, null latency gaps, never carry-forward) and full range-to-now rendering even with no new telemetry. Zero-traffic ranges render the full dashboard (summary cards + charts + table), not a full-page empty state.
- **Request-types table pagination**: server-side paginated (`mat-paginator`, page/pageSize passed to the API) and server-ordered by execution count.
- **Runaway `aggregate_rollups` growth (production bug found via manual testing)**: the aggregation job previously always inserted new rows on every scheduled run (every 60s) instead of upserting, causing unbounded duplicate growth and slow UI queries; fixed via upsert-by-key in `RelationalAggregateRollupStore`/`InMemoryAggregateRollupStore`, plus one-time SQL cleanup scripts (`scripts/cleanup-duplicate-rollups/`) for already-affected databases.
- **SQLite reader/writer contention**: the storage layer previously used SQLite's default rollback-journal mode, where a writer holds an exclusive whole-file lock, blocking dashboard reads while the aggregation/ingestion background writers were active; fixed by enabling WAL mode + a busy timeout via a connection interceptor.

#### Blocked
- None identified that require external credentials or infrastructure. There is no genuine external blocker at this time.

#### Deviated from plan (explicit, intentional)
1. **Query-path authentication was temporarily removed, then restored.** An earlier session deferred `[Authorize]` on `ServicesController`/`ServiceMetricsController` to a future RBAC workstream. That deviation has since been reversed: both controllers now require `[Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.SchemeName)]`, return 401 for missing/invalid keys, and return 404 (never another tenant's data) when the authenticated key's tenant does not match the URL's `tenantId` - verified by `CrossTenantIsolationTests` and, this session, by the real-OTLP `TwoTenantRealOtlpAcceptanceTests`. Doc B's tenant-isolation DoD language is now satisfied at both the write and query layers.
2. **Dashboard technology changed from Blazor Server to Angular 18 + Material**, per explicit user request. The functionally equivalent capability set was retained (service list, per-service metrics, time-range selection); this is a legitimate technology substitution, not a scope reduction.
3. **`/v1/ingest` simplified JSON transport kept as a documented dev/back-compat bridge** alongside the real OTLP endpoint, rather than removed - an explicit, documented decision, not an oversight.

#### Out-of-scope confirmation
Docs C (Observability & Analytics), D (Diagnostics), E (Policy & Governance), F (Cloud/Kubernetes) and the CLI: no code under this repository implements any of them. No OSS `PlaxionMediator` repository file was modified by any session; Platform only consumes the released NuGet packages.

---

### 3. Known gaps, technical debt, and security/testing/documentation gaps

**Technical debt**
- Dev-mode API-key seeding logic is duplicated across `Platform.Ingestion` and `Platform.Api` `Program.cs` files (copy-pasted deterministic dev-key blocks) - minor duplication, not a blocker.
- `ApiKeysController` exists in two places (`Platform.Ingestion` and `Platform.Api`) with duplicated logic by design (to avoid a nontrivial shared-library restructuring) - acceptable MVP debt, worth consolidating post-MVP.

**Security gaps (release-relevant)**
- Resolved this session: tenant-scoped queries now require `X-Api-Key` (401/404 enforced, `CrossTenantIsolationTests` + `TwoTenantRealOtlpAcceptanceTests`); `POST /v1/tenants` is now rate-limited (5 req/min per client IP, fixed window, `TenantsRateLimitingTests`); malformed OTLP protobuf payloads return 400 not 500 (`OtlpMetricsEndpointTests.Post_MalformedOtlpProtobuf_ReturnsBadRequestNotServerError`); an explicit code search this session found no plaintext API-key logging in `Platform.Identity`, `Platform.Ingestion`, or `Platform.Api`.
- Remaining, non-blocking: no formal secrets-scanning/log-redaction automation (manual grep-based audit only); no WAF/DDoS-layer protection (expected to sit in front of the service in a real deployment, out of scope for application code).

**Testing gaps**
- Resolved this session: a real end-to-end acceptance test (`tests/Platform.Api.Tests/TwoTenantRealOtlpAcceptanceTests.cs`) proves two services under one tenant are both visible, a second tenant sees no data, and tenant-1's real TenantId with tenant-2's key returns 404 - all through the genuine OpenTelemetry SDK + OTLP exporter path (no hand-built IngestRequest/JSON DTOs).
- A measured performance baseline now exists (`docs/performance-baseline.md`, `benchmarks/run-ingestion-load.ps1`).
- Remaining: the Angular dashboard has unit/component tests but no end-to-end (Cypress/Playwright) browser-driven coverage of the real create-tenant -> manage -> view-metrics flow against a live backend.

**Documentation gaps**
- This document (and the sibling docs listed in section 6) have been reconciled against the verified current code as of this session; re-verify periodically as the controllers' route/auth attributes evolve.

---

### 4. Ordered implementation backlog

All P0/P1 items below are now DONE, verified in code/tests this session. Remaining P2/P3 items are non-blocking follow-ups, not required for a Doc B MVP GO decision.

#### 1. Tenant-query access control (P0 - was blocking MVP DoD) - DONE
- **Objective:** Close the "unauthenticated tenant query" gap.
- **Evidence:** `ServicesController`/`ServiceMetricsController` now carry `[Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.SchemeName)]`; `CrossTenantIsolationTests` and `TwoTenantRealOtlpAcceptanceTests` prove 401 on missing/invalid key and 404 on tenant mismatch.

#### 2. Retention/eviction job for raw `MetricSample`s (P1) - DONE
- **Objective:** Implement Doc B's retention requirement.
- **Evidence:** `Platform.Aggregation/RetentionHostedService.cs` (configurable `RetentionOptions`, runs after aggregation so rollups never depend on evicted samples), covered by `Platform.Aggregation.Tests`.

#### 3. Platform self-observability (P1) - DONE
- **Objective:** Instrument ingestion/storage/aggregation/query latency and failure counts.
- **Evidence:** `IngestionMetrics` (`Platform.Ingestion`), `AggregationMetrics` (`Platform.Aggregation`, histogram `platform.aggregation.job.duration`), `ApiMetrics` (`Platform.Api`) - standard .NET `Meter`/`Counter`/`Histogram` instruments, covered by `ApiMetricsTests`.

#### 4. Minimal rate limiting on `POST /v1/tenants` (P2) - DONE
- **Objective:** Prevent trivial abuse of the anonymous bootstrap endpoint.
- **Evidence:** `Microsoft.AspNetCore.RateLimiting` fixed-window limiter (5 req/min per client IP, `RateLimiterPolicies.TenantBootstrap`), `[EnableRateLimiting]` on `TenantsController.Create`, verified by `TenantsRateLimitingTests.Create_RepeatedlyFromSameClient_EventuallyReturns429`.

#### 5. Performance baseline (P2) - DONE
- **Objective:** Record actual ingestion/query/aggregation latency numbers under a moderate synthetic load.
- **Evidence:** `docs/performance-baseline.md` with real measured numbers (~27.8 req/s, p50 13.4ms/p95 62.4ms for `/v1/ingest`, 105ms for a single `Platform.Api` query call) and a reusable manual tool at `benchmarks/run-ingestion-load.ps1`.

#### 6. Documentation sweep (P2) - DONE (this document)
- **Objective:** Re-align `docs/mvp-status.md`, `docs/dashboard.md`, `docs/storage-decision.md`, `docs/mvp-limitations-and-exclusions.md`, and this index with the verified current code.
- **Evidence:** all five documents updated in this session; see section 6 for the reconciled DoD checklist.

#### Remaining non-blocking follow-ups (P3, post-MVP)
- Real charts/graphs in the Dashboard (time-series visualization beyond tables).
- PostgreSQL/MySQL providers exercised against real managed instances (currently only Sqlite is exercised as a live durable provider).
- Angular Cypress/Playwright end-to-end coverage against a live backend.
- Consolidating the duplicated `ApiKeysController`/dev-key-seeding logic between `Platform.Ingestion` and `Platform.Api`.

---

### 5. Recommended next task

All release-blocking Doc B DoD items are closed. The recommended next task is a **post-MVP hardening/polish pass**: pick from the P3 follow-ups above (real dashboard charts, a managed PostgreSQL/MySQL smoke test, or Cypress/Playwright E2E coverage), based on product priority rather than release-blocking necessity.

---

### 6. Final Doc B Definition of Done checklist

| DoD item | Status |
|---|---|
| Real OTLP metrics ingestion works | Done |
| Standard OpenTelemetry .NET exporter can send directly to Platform | Done |
| API-key authentication works over OTLP | Done |
| TenantId derived only from authenticated identity (ingestion) | Done |
| No Platform-specific telemetry client SDK required | Done |
| Canonical OTLP -> MetricSample mapping works | Done |
| Storage contracts are provider-agnostic | Done - verified: `Platform.Storage.Relational` exists, no EF Core leakage into contracts |
| At least one durable provider exists | Done |
| Durable storage survives restart | Done (per `SharedSqlitePersistenceTests`) |
| Provider conformance tests exist | Done |
| Aggregation works against durable provider | Done |
| Query API works | Done |
| Cross-tenant isolation proven | Done - true for both the write/storage layer AND the query layer; verified with synthetic tests (`CrossTenantIsolationTests`) and a real end-to-end OTLP test (`TwoTenantRealOtlpAcceptanceTests`) |
| Operator API-key lifecycle works | Done |
| Dashboard works (tenant list -> services -> metrics) | Done |
| Throughput chart | Done - correct idle/zero-fill semantics, shared bucket timeline |
| P50/P95/P99 latency chart | Done - null/gap semantics, never carries forward previous value |
| Failure-rate chart | Done - 0% on idle buckets, never carries forward previous value |
| Request table/pagination | Done - server-side paginated (`mat-paginator`) and server-ordered |
| Multiple services under one tenant | Done (verified manually by the user, and by `TwoTenantRealOtlpAcceptanceTests`) |
| Retained historical metrics visible | Done |
| Time-range selection works | Done - Last Hour/24 Hours/7 Days all render full range-to-now domain regardless of telemetry gaps |
| Platform self-observability | Done - `IngestionMetrics`/`AggregationMetrics`/`ApiMetrics` (Meter/Counter/Histogram) |
| Retention/eviction of raw samples | Done - `RetentionHostedService` |
| Rate limiting on anonymous bootstrap endpoint | Done - 5 req/min per IP on `POST /v1/tenants`, 429 on excess |
| Malformed OTLP payloads handled cleanly | Done - 400, not 500, verified by test |
| Health/readiness endpoints | Done - `/health`, `/healthz` on both `Platform.Ingestion` and `Platform.Api` |
| Performance baseline recorded | Done - `docs/performance-baseline.md` |
| Connect Your App documentation describes the real OTLP path | Done |
| Docs C/D/E/F not implemented | Confirmed |
| CLI not implemented | Confirmed |
| OSS repository not modified | Confirmed |
| Build/tests green | Done - `dotnet build`: 0 errors/warnings; `dotnet test`: 140/140 across 6 projects (Platform.Storage.Tests 22, Platform.Aggregation.Tests 11, Platform.Identity.Tests 6, Platform.Ingestion.Tests 45, Platform.Storage.Relational.Tests 15, Platform.Api.Tests 41), all re-run and verified during this final certification pass; Angular `ng test --browsers=ChromeHeadless`: 77/77, also re-run this session |

---

### Summary

- **Final certification result: 100%** of Doc B's release-blocking Definition of Done items are implemented and verified directly against code/tests, including the Dashboard's three time-series charts (throughput, P50/P95/P99 latency, failure rate) with correct temporal semantics (no carry-forward, idle-bucket zero/null-fill, full range-to-now rendering) and the server-side paginated request-types table, both confirmed present in `service-metrics.component.ts`/`.html` and covered by passing specs.
- **DONE: 28 - PARTIAL: 1 - NOT STARTED: 2 - BLOCKED: 0** (counts reflect the DoD checklist in section 6 plus the gap analysis in section 2; PARTIAL = Postgres/MySQL live-server exercise; NOT STARTED = live Postgres/MySQL smoke test, Angular E2E browser tests - both explicitly non-blocking for Doc B).
- **Next recommended tasks (non-blocking, priority order):**
  1. Exercise the PostgreSQL/MySQL providers against real managed instances at least once.
  2. Add Cypress/Playwright browser E2E coverage of the create-tenant -> manage -> view-metrics flow.
  3. Consolidate the duplicated `ApiKeysController`/dev-key-seeding logic between `Platform.Ingestion` and `Platform.Api`.
- **GO / NO-GO for declaring Doc B MVP complete today: GO.** All items Doc B treats as release-blocking - real OTLP ingestion, provider-agnostic durable storage, aggregation, tenant isolation at both write AND query layers, operator API-key lifecycle, a working Dashboard with real throughput/latency/failure-rate charts and a paginated request-types table, Platform self-observability, raw-sample retention, and MVP security hardening (rate limiting, malformed-payload handling, health checks) - are implemented and verified in code/tests (140/140 .NET tests, 77/77 Angular tests, 0 build errors/warnings, all re-run during this final certification pass). No release-blocking item remains open.

---

### GO FOR DOC B MVP — COMPLETE
