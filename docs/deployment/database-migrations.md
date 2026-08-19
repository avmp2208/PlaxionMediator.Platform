# Database migrations

## Decision summary

| Topic | Decision |
|-------|----------|
| Schema tool | EF Core Migrations (replaces production reliance on `EnsureCreated`) |
| Providers with migrations | **Sqlite** (local/tests) and **PostgreSQL** (production target) |
| MySQL | Still registered; schema uses legacy `EnsureCreated` until a MySQL migrations set is added (documented limitation) |
| Dev auto-migrate | **Yes** — Development/Testing call `Database.MigrateAsync()` on startup |
| Production auto-migrate | **No** by default — avoids multi-replica migration races |
| Explicit migrate job | `dotnet Platform.Api.dll --migrate` or `dotnet Platform.Ingestion.dll --migrate` |
| History table | `__EFMigrationsHistory` (standard EF Core) |

### Rationale (execution policy)

Applying migrations from every replica at startup races on DDL and is unsafe under rolling deploys. The safest multi-instance pattern is:

1. Run a **single** one-shot job with `--migrate` against the target database.
2. Start N API/Ingestion replicas only after migrations succeed.
3. Replicas use `ValidateOnly` mode: connect + assert no pending migrations; fail fast if someone forgot step 1.

Override with `Database:AutoMigrate=true` only for break-glass or single-instance demos.

## Layout

Migrations live in `src/Platform.Storage.Relational/Migrations/`:

```
Migrations/
  Sqlite/
    YYYYMMDDHHMMSS_Initial.cs
    YYYYMMDDHHMMSS_Initial.Designer.cs
    PlatformDbContextModelSnapshot.cs
  PostgreSql/
    YYYYMMDDHHMMSS_Initial.cs
    ...
```

Both sets represent the **current certified Doc B schema** (tables: `metric_samples`, `aggregate_rollups`, `services`, `api_keys`, including `RequestType`).

Because EF Core migrations are provider-specific, Sqlite and PostgreSQL each have their own Initial migration and model snapshot. A custom `NamespaceFilteredMigrationsAssembly` ensures only the active provider’s migrations are discovered at runtime.

### MySQL limitation

MySQL remains selectable via `Storage:Provider=MySql` and still initializes via `EnsureCreated` + lightweight column patches. **Do not use MySQL for production schema versioning** until a dedicated migrations folder is added the same way as Sqlite/PostgreSQL.

## Generating migrations (developers)

```powershell
# Sqlite
dotnet ef migrations add <Name> `
  --context PlatformDbContext `
  --project src/Platform.Storage.Relational `
  --output-dir Migrations/Sqlite `
  -- --provider Sqlite

# PostgreSQL (temporarily move Sqlite migrations out of the project tree first
# so EF does not see the other provider's "Initial" name/snapshot, then move back)
dotnet ef migrations add <Name> `
  --context PlatformDbContext `
  --project src/Platform.Storage.Relational `
  --output-dir Migrations/PostgreSql `
  -- --provider PostgreSql
```

Design-time factory: `PlatformDbContextFactory` (`--provider Sqlite|PostgreSql|MySql`).

## Applying migrations

### Development

Default: auto-migrate on startup when `Storage:Provider` is Sqlite/PostgreSql.

```powershell
$env:ASPNETCORE_ENVIRONMENT="Development"
$env:Storage__Provider="Sqlite"
$env:ConnectionStrings__Platform="Data Source=.data/platform-dev.db"
dotnet run --project src/Platform.Api
```

### Production / Staging (recommended)

```bash
# 1) Migrate once
ASPNETCORE_ENVIRONMENT=Production \
Storage__Provider=PostgreSql \
ConnectionStrings__Platform="Host=...;Database=platform;Username=...;Password=..." \
dotnet Platform.Api.dll --migrate

# 2) Start hosts (no --migrate)
dotnet Platform.Api.dll
dotnet Platform.Ingestion.dll
```

`--migrate` applies pending EF migrations and **exits**. It does not start Kestrel traffic serving.

### Config knobs

| Setting | Effect |
|---------|--------|
| `Database:AutoMigrate=true` | Force `Migrate` even in Production |
| `Database:AutoMigrate=false` | Never auto-migrate; use ValidateOnly/None per `FailOnPendingMigrations` |
| `Database:FailOnPendingMigrations=true` (default) | Production startup fails if pending migrations remain |

## Initial schema

The Initial migration creates:

- `metric_samples` (incl. nullable `RequestType`)
- `aggregate_rollups` (incl. nullable `RequestType`, service/metric indexes)
- `services` (unique tenant+name+environment)
- `api_keys` (hashed keys only)
- `__EFMigrationsHistory`

No tenant seed data is part of migrations. Development API-key bootstrap is separate and **disabled in Production**.

## Safety tests

See `tests/Platform.Api.Tests/DeploymentReadinessTests.cs`:

- Fresh DB initialized only through migrations (`__EFMigrationsHistory` present)
- Insert Tenant/ApiKey/Service/MetricSample/AggregateRollup data and re-read
- Production missing connection string / unsupported provider fail startup
- There is no dev seed anymore; every environment provisions tenants/keys via `POST /v1/tenants`
