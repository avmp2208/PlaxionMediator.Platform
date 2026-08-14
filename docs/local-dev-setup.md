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

In the `Development` environment:
- Storage defaults to **in-memory**.
- `Program.cs` seeds a local-dev API key bound to `DevTenant` and writes the
  plaintext to `src/Platform.Ingestion/.dev-api-key` (**never logged**).

```powershell
Get-Content src/Platform.Ingestion/.dev-api-key
```

Use that key for `POST /v1/metrics` (OTLP) or `POST /v1/ingest` (JSON bridge).

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
