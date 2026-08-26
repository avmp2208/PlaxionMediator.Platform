# Architecture Overview (MVP)

## Projects

| Project | Type | Status | Responsibility |
|---|---|---|---|
| `Platform.Storage` | class library | Done | Provider-neutral canonical models (`Tenant`, `Service`, `MetricSample`, `AggregateRollup`, `ApiKey`, `TenantMembership`, `TenantInvitation`) + store interfaces + in-memory implementations. |
| `Platform.Storage.Relational` | class library | Done | EF Core implementation for SQLite and PostgreSQL (versioned migrations); MySQL implemented via `EnsureCreated` only, no migrations assembly — not chart-supported. |
| `Platform.Hosting` | class library | Done | Shared DI helper `AddPlatformStorage` (InMemory vs. Sqlite vs. MySql vs. PostgreSql selection) + configuration binding (`PlatformOptions` and related option classes). |
| `Platform.Identity` | class library + ASP.NET Core auth | Done | API-key hashing/service + `ApiKeyAuthenticationHandler` — **telemetry ingestion auth only** (`tenant_id` claim from `X-Api-Key`). Human sign-in uses OIDC, wired directly in `Platform.Api`/`Platform.Ingestion`, not this project. |
| `Platform.Ingestion` | ASP.NET Core Web API | Done | `POST /v1/metrics` (OTLP protobuf), `POST /v1/ingest` (JSON bridge), `/v1/api-keys` admin, `/health`, `/health/ready`. Auth: `X-Api-Key` only — OIDC does not apply to this service. |
| `Platform.Aggregation` | class library | Done | `RollupAggregator.Compute` + `AggregationHostedService` (scheduled) + `RetentionHostedService` (raw-sample eviction). |
| `Platform.Api` | ASP.NET Core Web API | Done | Tenants, tenant memberships, invitations, API keys (dual API-key/JWT-Owner auth), `GET /v1/tenants/{tenantId}/services*`, `/health`, `/health/ready`; hosts the aggregation job; serves the built Angular SPA from `wwwroot`. Auth: OIDC JWT Bearer for human callers, API key for the dual-auth API-keys endpoints. |
| `platform-dashboard-web` | Angular 18 (Material, dark mode) | Done | Operator UI: tenants, memberships, invitations, per-service metrics/charts, paginated request-type table. OIDC-first sign-in (Authorization Code + PKCE via `angular-oauth2-oidc`), with a legacy anonymous/API-key fallback when `authentication.enabled=false`. |

## Two independent auth surfaces

Platform has **two separate authentication mechanisms for two separate
audiences** — they are not layered on top of each other and neither
replaced the other:

- **Telemetry ingestion (machine-to-machine)**: `X-Api-Key` only, handled by
  `Platform.Identity`. `TenantId` is derived exclusively from the
  authenticated key. OIDC has no role here.
- **Human sign-in (Dashboard/API)**: generic OIDC — Authorization Code +
  PKCE from the Angular SPA, JWT Bearer validation in `Platform.Api`
  (`Authentication:Authority`/`Authentication:Audience` driving standard
  `.well-known/openid-configuration` + JWKS discovery, no provider-specific
  code). `MapInboundClaims=false` preserves short claim names
  (`iss`/`sub`/`role`). The `/v1/api-keys` endpoints accept **either**
  mechanism (API key, or JWT with the tenant's Owner role) so operators can
  manage telemetry keys via either path.
- A legacy anonymous mode (`authentication.enabled=false`) is retained: the
  Dashboard falls back to a `localStorage`-backed tenant registry and
  per-tenant API-key session, with no OIDC login. Production refuses to
  start in this mode unless `AllowAnonymousProduction=true` is explicitly
  set.

## Data flow

```
 instrumented app --X-Api-Key--> POST /v1/metrics (OTLP) or /v1/ingest (JSON bridge)
                                        |
                                        v
                    OtlpMetricsMapper / OtlpToCanonicalMapper -> MetricSample
                    (size limit 1 MiB; cardinality guards on samples / metric names)
                                        |
                                        v
              IMetricSampleStore / IServiceCatalog / IApiKeyStore  (InMemory, Sqlite, PostgreSql; MySql partial)
                                        |
                          AggregationHostedService (scheduled, on Platform.Api)
                                        |
                                        v
                    IAggregateRollupStore  (throughput, P50/P95/P99, failure rate)
                                        |
                                        v
             GET /v1/tenants/{tenantId}/services , .../services/{id}/metrics (Platform.Api)
                                        |
                    Bearer JWT (OIDC) — resolves TenantMembership.Role server-side
                    [or X-Api-Key, legacy anonymous mode only]
                                        |
                                        v
                  platform-dashboard-web (Angular)


 human user --OIDC Authorization Code + PKCE--> Dashboard login
                                        |
                                        v
        Platform.Api validates JWT (Authority/Audience/JWKS) --> TenantMembership lookup
                                        |
                                        v
        TenantAuthorizationService.RoleGrants(role, permission) -> Owner: all; Member: ViewTenant only
```

## Tenant model

- `TenantId` is attached to every `MetricSample`, `Service`,
  `AggregateRollup`, `ApiKey`, `TenantMembership`, and `TenantInvitation`.
- For **ingestion**, the only way to obtain a `TenantId` for a request is
  through a validated API key; OTLP resource attributes never supply
  tenancy.
- For **human access**, `TenantId` is resolved via `TenantMembership`
  (keyed on `(TenantId, IdentityIssuer, IdentitySubject)`), never trusted
  from the URL alone — a mismatch between the authenticated identity's
  membership and the URL's `tenantId` returns 404, not 403, specifically to
  avoid tenant-ID enumeration.
- Stores filter by `TenantId` at the query boundary.
- Tenant-local authorization (`TenantAuthorizationService`) is intentionally
  a coarse two-tier permission model — **Owner** (view + manage invitations
  + manage API keys) and **Member** (view only) — not a full RBAC
  framework, and is cleanly separate from the global tenant-*creation*
  policy (`Authorization:TenantCreation`, modes `AnyAuthenticatedUser` /
  `Role` / `Claim`).
- **PlatformAdministrator** break-glass authorization exists for
  cross-tenant operational access, default mode `Disabled`.

## Invitations

Owners create invitations (multi-tenant batch, all-or-nothing pre-check),
which are just-in-time claimed into a `Member` `TenantMembership` on the
invitee's next `GET /v1/tenants` call, matched by the OIDC email claim. No
email is sent by the platform — invitees must be told out-of-band. See
`docs/tenant-invitations-and-roles.md` for the full contract.

## Storage selection

See `docs/storage-decision.md`. Development/Testing → in-memory by default;
set `Storage:Provider=Sqlite|PostgreSql|MySql` + `ConnectionStrings:Platform`
for durable multi-process deployment. Production/Staging/QA refuse
`InMemory`/`Sqlite`. **PostgreSQL is the only relational provider the
official Helm chart exposes** — SQLite is a durable local/single-instance
option at the application level but not chart-deployable; MySQL is
implemented but has no migrations assembly and is not chart-deployable.
This choice is entirely internal to Ingestion/Api — instrumented apps
pushing metrics via `/v1/metrics` never see or configure a database
provider.

## Default local ports (launchSettings)

| App | URL |
|---|---|
| Platform.Ingestion | `http://localhost:5292` |
| Platform.Api | `http://localhost:5095` |
| platform-dashboard-web (`ng serve`) | `http://localhost:4200` |

## Scope authority

This document describes current implementation architecture. For the
frozen statement of MVP scope (supported / not supported / self-hosted
preview / future), see `platform-mvp-contract.md` in the documentation
repository.
