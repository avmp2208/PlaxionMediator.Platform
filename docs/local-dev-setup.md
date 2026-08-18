# Local Development Setup

## Prerequisites
- .NET 9 SDK
- (Optional) an IDE such as Rider or Visual Studio

## Building and testing

```powershell
cd PlaxionMediator.Platform
dotnet build
dotnet test
```

All tests should pass (currently **60** across Storage, Storage.Sqlite, Ingestion,
Identity, Aggregation, Api — the .NET solution no longer contains a Dashboard
project; see below for the separate Angular Dashboard).

## Running Platform.Ingestion

```powershell
dotnet run --project src/Platform.Ingestion
```

Default URL: `http://localhost:5292`.

In the `Development` environment, storage defaults to **in-memory** (Sqlite is
also allowed locally — see below). `InMemory` and `Sqlite` are
**local-development-only** providers; Staging/QA/Production must use
`PostgreSql` or `MySql` and fail fast at startup otherwise.

There is no seeded/dev API key. Provision a tenant + key first via
`POST /v1/tenants` with `{ "name": "my-org" }` — the response includes a
`plaintextKey` shown once (never logged, never persisted in plaintext). Use
that key for `POST /v1/metrics` (OTLP) or `POST /v1/ingest` (JSON bridge).

Health: `GET /health` or `GET /healthz`.

### Optional: durable local SQLite

```json
{
  "Storage": { "Provider": "Sqlite" },
  "ConnectionStrings": {
    "Platform": "Data Source=./platform-dev.db"
  }
}
```

Use the **same** connection string for Platform.Api if you want shared data across processes.

## Running Platform.Api

```powershell
dotnet run --project src/Platform.Api
```

Default URL: `http://localhost:5095`.

Platform.Api also hosts `AggregationHostedService` (scheduled rollups).

Health: `GET /health` or `GET /healthz`.

## Running the Dashboard (Angular)

```powershell
cd src/platform-dashboard-web
npm install
ng serve
```

Default URL: `http://localhost:4200`.

The Dashboard is wired to `Platform.Api`. Ensure `Platform.Api` is running and CORS is configured (default `http://localhost:4200` is allowed in `appsettings.json`). See `docs/dashboard.md` for details.

## Ports
| App | URL |
|---|---|
| Platform.Ingestion | `http://localhost:5292` |
| Platform.Api | `http://localhost:5095` |
| platform-dashboard-web (`ng serve`) | `http://localhost:4200` |

Override .NET ports with `--urls` as needed.

## Operator API keys
With any valid key for a tenant (on Ingestion):
- `POST /v1/api-keys` — create (plaintext once)
- `GET /v1/api-keys` — list metadata
- `DELETE /v1/api-keys/{id}` — revoke
