# Architecture Overview (MVP)

## Projects

| Project | Type | Status | Responsibility |
|---|---|---|---|
| `Platform.Storage` | class library | Done | Provider-neutral canonical models + store interfaces + in-memory implementations. |
| `Platform.Storage.Relational` | class library | Done | EF Core provider implementing Storage interfaces for SQLite, MySQL and PostgreSQL (EF types stay here). |
| `Platform.Hosting` | class library | Done | Shared DI helper `AddPlatformStorage` (InMemory vs Sqlite vs MySql vs Postgres selection). |
| `Platform.Identity` | class library + ASP.NET Core auth | Done | API-key hashing/service + `ApiKeyAuthenticationHandler` (`tenant_id` claim from `X-Api-Key`). |
| `Platform.Ingestion` | ASP.NET Core Web API | Done | `POST /v1/metrics` (OTLP protobuf), `POST /v1/ingest` (JSON bridge), `/v1/api-keys` admin, `/health`. |
| `Platform.Aggregation` | class library | Done | `RollupAggregator.Compute` + `AggregationHostedService` (scheduled). |
| `Platform.Api` | ASP.NET Core Web API | Done | `GET /v1/services`, `GET /v1/services/{id}/metrics`; hosts aggregation job; `/health`. |
| `platform-dashboard-web` | Angular (Material, dark mode) | **In Progress** | Operator UI; **UI only** so far — renders against mock data, not yet wired to Platform.Api. Replaces the removed Blazor `Platform.Dashboard` project. |

## Data flow

```
 instrumented app --X-Api-Key--> POST /v1/metrics (OTLP) or /v1/ingest (JSON bridge)
                                        |
                                        v
                    OtlpMetricsMapper / OtlpToCanonicalMapper -> MetricSample
                    (size limit 1 MiB; cardinality guards on samples / metric names)
                                        |
                                        v
              IMetricSampleStore / IServiceCatalog / IApiKeyStore  (InMemory, Sqlite, MySql or Postgres)
                                        |
                          AggregationHostedService (scheduled, on Platform.Api)
                                        |
                                        v
                             IAggregateRollupStore
                                        |
                                        v
             GET /v1/services , GET /v1/services/{id}/metrics (Platform.Api)
                                        |
                         X-Api-Key over HTTP (session-stored in browser)
                                        |
                                        v
                  platform-dashboard-web (Angular) -- NOT YET WIRED (mock data only)
```

## Tenant model
- `TenantId` is attached to every `MetricSample`, `Service`, `AggregateRollup`, and `ApiKey`.
- The only way to obtain a `TenantId` for a request is through a validated API key.
- OTLP resource attributes never supply tenancy.
- Stores filter by `TenantId` at the query boundary; cross-tenant reads return empty/404.
- Dashboard never sees or stores `TenantId`; it only holds the API key and lets Platform.Api resolve tenancy (this call is not implemented yet — see `docs/dashboard.md`).

## Storage selection
See `docs/storage-decision.md`. Development/Testing → in-memory by default;
set `Storage:Provider=Sqlite|MySql|Postgres` + shared `ConnectionStrings:Platform` for multi-process durability.
This choice is entirely internal to Ingestion/Api — instrumented apps pushing metrics via `/v1/metrics` never see or configure a database provider.

## Default local ports (launchSettings)
| App | URL |
|---|---|
| Platform.Ingestion | `http://localhost:5292` |
| Platform.Api | `http://localhost:5095` |
| platform-dashboard-web (`ng serve`) | `http://localhost:4200` |
