# Backend configuration reference

This document describes every configuration key used by **Platform.Api** and **Platform.Ingestion** for deployment readiness. Values layer as:

1. `appsettings.json`
2. `appsettings.{Environment}.json` (`Development`, `Testing`, `Staging`, `Production`)
3. Environment variables (double-underscore `__` → `:`)
4. Command-line args

No cloud secret stores (Azure Key Vault / AWS SM / K8s secrets controllers) are integrated. Supply secrets via env vars or mounted config files.

## Quick production example (no real credentials)

```bash
export ASPNETCORE_ENVIRONMENT=Production
export Storage__Provider=PostgreSql
export ConnectionStrings__Platform="Host=db.example.com;Port=5432;Database=platform;Username=platform;Password=***"
export Cors__AllowedOrigins__0="https://app.example.com"
export Platform__PublicBaseUrl="https://api.example.com"
export Aggregation__Interval="00:01:00"
export Retention__RawSampleRetention="7.00:00:00"

# One-shot schema migration (run once before scaling replicas):
dotnet Platform.Api.dll --migrate

# Then start the API:
dotnet Platform.Api.dll
```

---

## Platform

| Key | Required | Default | Secret? | Notes |
|-----|----------|---------|---------|-------|
| `Platform:Environment` | No | `""` | No | Logical label; may mirror `ASPNETCORE_ENVIRONMENT`. |
| `Platform:PublicBaseUrl` | No | `null` | No | Public base URL for diagnostics. Env: `Platform__PublicBaseUrl`. |

## Storage

| Key | Required | Default | Secret? | Notes |
|-----|----------|---------|---------|-------|
| `Storage:Provider` | Yes (validated) | `InMemory` in base appsettings | No | `InMemory`, `Sqlite`, `MySql`, `PostgreSql` (alias `Postgres`). **`InMemory` and `Sqlite` are local-development-only; Production/Staging/QA reject both and require `PostgreSql` or `MySql`.** Env: `Storage__Provider`. |

### Dev vs prod behavior

| Environment | Unset provider | Explicit `InMemory`/`Sqlite` | Durable server provider without CS |
|-------------|----------------|-------------------------------|-------------------------------------|
| Development / Testing | InMemory | InMemory / Sqlite (local file default) | Dev default file `platform.db` (Sqlite only) |
| Production / Staging / QA | Durable (fails without CS) | **Startup failure** | **Startup failure** |

## ConnectionStrings

| Key | Required | Default | Secret? | Notes |
|-----|----------|---------|---------|-------|
| `ConnectionStrings:Platform` | Required for durable providers in Production/Staging | Dev samples only | **Yes** (may contain password) | Env: `ConnectionStrings__Platform`. Never log the full value. |

Examples:

```
# Sqlite (path relative to process working directory — container-friendly)
ConnectionStrings__Platform=Data Source=/data/platform.db

# PostgreSQL
ConnectionStrings__Platform=Host=postgres;Port=5432;Database=platform;Username=platform;Password=***
```

## Database (migrations)

| Key | Required | Default | Secret? | Notes |
|-----|----------|---------|---------|-------|
| `Database:AutoMigrate` | No | `null` → auto only in Development/Testing | No | `true` forces Migrate at startup; `false` disables auto-migrate. Env: `Database__AutoMigrate`. |
| `Database:FailOnPendingMigrations` | No | `true` | No | In Production/Staging, fail startup when pending migrations exist (unless `--migrate` was used). |

See [database-migrations.md](./database-migrations.md).

## Aggregation

| Key | Required | Default | Secret? | Notes |
|-----|----------|---------|---------|-------|
| `Aggregation:Interval` | No | `00:01:00` | No | Must be positive. Env: `Aggregation__Interval`. |
| `Aggregation:BucketSize` | No | `00:01:00` | No | Must be positive. Env: `Aggregation__BucketSize`. |

## Retention

| Key | Required | Default | Secret? | Notes |
|-----|----------|---------|---------|-------|
| `Retention:RawSampleRetention` | No | `7.00:00:00` | No | How long raw samples are kept. Must be positive. |
| `Retention:CheckInterval` | No | `00:05:00` | No | Retention job cadence. Must be positive. |

## Ingestion

| Key | Required | Default | Secret? | Notes |
|-----|----------|---------|---------|-------|
| `Ingestion:MaxRequestSizeBytes` | No | `1048576` (1 MiB) | No | Kestrel + form limit. |
| `Ingestion:MaxSamplesPerRequest` | No | `5000` | No | Documented guardrail (JSON mapper constants align with defaults). |
| `Ingestion:MaxMetricNamesPerRequest` | No | `200` | No | Documented cardinality guardrail. |

## Cors (Platform.Api only)

| Key | Required | Default | Secret? | Notes |
|-----|----------|---------|---------|-------|
| `Cors:AllowedOrigins` | Required in non-Dev for browser clients | Dev defaults to localhost:4200/4600 | No | String array. Env: `Cors__AllowedOrigins__0=https://app.example.com`. |

Ingestion does **not** enable CORS (OTLP is not a browser client).

## Security

There is no dev/seeded API key in any environment. Every tenant (including local
development) provisions its own tenant + API key via the self-service
`POST /v1/tenants` endpoint; the plaintext key is returned once and never logged
or persisted.

## Observability

| Key | Required | Default | Secret? | Notes |
|-----|----------|---------|---------|-------|
| `Observability:OtlpEndpoint` | No | `null` | No | Reserved for platform self-telemetry. |
| `Observability:OtlpHeaders` | No | `null` | **Yes** if used | Prefer env var `Observability__OtlpHeaders`. Never commit. |

## Logging / host

| Key | Required | Default | Secret? | Notes |
|-----|----------|---------|---------|-------|
| `Logging:LogLevel:*` | No | Information / Warning | No | Standard ASP.NET Core. |
| `AllowedHosts` | No | `*` | No | Tighten in production as needed. |
| `ASPNETCORE_ENVIRONMENT` | Recommended | Development (local) | No | `Development`, `Testing`, `Staging`, `Production`. |
| `ASPNETCORE_URLS` | Recommended in containers | launchSettings / defaults | No | e.g. `http://+:8080`. |

## Startup validation (fail-fast)

On host start, options are validated (`ValidateOnStart`):

- `Storage:Provider` must be a supported value
- Aggregation / Retention intervals must be positive
- Ingestion payload limits must be positive
- Production/Staging/QA + missing `ConnectionStrings:Platform` → startup exception
- Production/Staging/QA + `InMemory` → startup exception
- Production/Staging/QA + `Sqlite` → startup exception (only `PostgreSql`/`MySql` are allowed)
- Production/Staging/QA + pending EF migrations (when `FailOnPendingMigrations=true`) → startup exception

A single non-secret startup log line reports environment, storage provider, schema status, aggregation/retention settings, and version. Connection strings and API keys are never logged.

## Health endpoints

| Path | Kind | Behavior |
|------|------|----------|
| `GET /health` | Liveness | Always 200 if process is up (no dependency checks). |
| `GET /healthz` | Liveness | Same as `/health`. |
| `GET /health/ready` | Readiness | Includes DB connectivity when relational storage is registered. No secrets in response body. |

## Container contract

Hosts run with:

- Config files and/or environment variables only
- Network reachability to the database (when durable)
- **No** dependency on Rider, repo-relative absolute user paths, or user profile folders

SQLite paths should be absolute container paths (e.g. `/data/platform.db`) or relative to the process working directory. `InMemory` and `Sqlite` are local-development-only providers: `Storage:Provider` must be `PostgreSql` or `MySql` in Staging/QA/Production, and the host fails fast at startup otherwise.
