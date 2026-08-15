# PlaxionMediator.Platform

Multi-tenant observability platform (Platform MVP), built per
`documentation/strategy-v2/platform/B-platform-mvp-execution-plan.md`.

## Status

Doc B backend (ingestion, storage, identity, aggregation, query API) is
implemented and test-verified — **Done**. The operator Dashboard was
re-platformed from Blazor Server to **Angular + Angular Material** and is
wired to `Platform.Api` (`/v1/services`, `/v1/services/{id}/metrics`). The
dashboard's landing page is now a **tenant-list-first flow** (self-service
tenant + API key creation) — **In Progress**, pending the backend
`POST /v1/tenants` and `/v1/api-keys` endpoints. See `docs/mvp-status.md`
for the authoritative done/remaining list per component.

## Solution layout

```
src/Platform.Ingestion/       [Done]        OTLP/HTTP + JSON ingest, API-key admin, health
src/Platform.Api/             [Done]        Query API + scheduled aggregation, health
src/platform-dashboard-web/   [In Progress] Angular Material UI (dark mode), wired to Platform.Api — tenant-list-first flow pending backend /v1/tenants + /v1/api-keys
src/Platform.Storage/         [Done]        Canonical models + store interfaces + in-memory
src/Platform.Storage.Relational/ [Done]        EF Core storage provider (SQLite, MySQL, PostgreSQL)
src/Platform.Identity/        [Done]        API-key hashing + auth handler
src/Platform.Aggregation/     [Done]        Rollup compute + hosted service
src/Platform.Hosting/         [Done]        Shared storage DI wiring
tests/                        [Done]        xUnit projects per .NET component
docs/                                       Status, contracts, architecture, connect guide
```

> Note: the previous `src/Platform.Dashboard` (Blazor Server) project and
> `tests/Platform.Dashboard.Tests` have been **removed** in favor of the
> Angular app above.

## Running the backend

```powershell
dotnet build
dotnet test

dotnet run --project src/Platform.Ingestion   # http://localhost:5292
dotnet run --project src/Platform.Api         # http://localhost:5095
```

Local-dev API key (Development): `Get-Content src/Platform.Ingestion/.dev-api-key`

## Running the Dashboard (Angular)

```powershell
cd src/platform-dashboard-web
npm install
ng serve   # http://localhost:4200
```

The Dashboard is wired to `Platform.Api`. Its landing page (`/`) lists
tenants created on this browser and lets you create a new tenant (which
generates an API key to share with the service that will send metrics);
"Manage" signs you in and takes you to the per-service metrics views. See
`docs/dashboard.md`.

## Docs
- `docs/mvp-status.md` — what is done vs remaining, per component
- `docs/connect-your-app.md` — OTLP ingestion usage
- `docs/dashboard.md` — Angular Dashboard (status, pages, run/test)
- `docs/api-contracts.md` — HTTP contracts
- `docs/local-dev-setup.md` — ports and SQLite wiring
