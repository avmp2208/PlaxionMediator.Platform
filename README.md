# PlaxionMediator.Platform

Self-hosted, multi-tenant telemetry platform for apps instrumented with
`PlaxionMediator`. It ingests OTLP metrics over HTTP, stores and aggregates
them (throughput, P50/P95/P99 latency, failure rate) per tenant and service,
and shows them in a web dashboard — all running in your own Kubernetes
cluster with your own PostgreSQL database.

Human sign-in uses OIDC (bring your own identity provider); telemetry
ingestion uses per-tenant API keys.

## Running the backend

```powershell
dotnet build
dotnet test

dotnet run --project src/Platform.Ingestion   # http://localhost:5292
dotnet run --project src/Platform.Api         # http://localhost:5095
```

Local development defaults to in-memory/SQLite storage — no external
database required to get started.

## Running the Dashboard (Angular)

```powershell
cd src/platform-dashboard-web
npm install
ng serve   # http://localhost:4200
```

The Dashboard is wired to `Platform.Api`: sign in, view your tenants and
services, and see per-service throughput/latency/failure-rate charts.
