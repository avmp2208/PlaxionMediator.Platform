### Storage product decision

#### Requirements considered
- Append-heavy write pattern (metric samples arrive continuously).
- Time-range queries per tenant/service/metric.
- Mandatory tenant partitioning on every read/write.
- Scheduled (non-streaming) rollups reading potentially all recent samples.
- Moderate MVP traffic (single-digit services, dev/test scale) - not high-volume production yet.
- Operational simplicity: must run without requiring the developer to stand up extra infrastructure.
- **Provider-agnostic core**: callers never depend on EF Core or a specific database.

#### Candidates considered
- **PostgreSQL via EF Core/Npgsql** - production-grade, best long-term choice, but requires a running Postgres instance (docker/local service) which adds friction for `dotnet test`/`dotnet run` in a fresh clone and CI.
- **SQLite via EF Core** - zero-infrastructure, file- or in-memory-backed, fully compatible with EF Core's provider model so migrating to Postgres later is a low-risk swap (same LINQ queries, same `DbContext`), acceptable for MVP write/query volumes.
- **Pure in-memory (`ConcurrentBag`/`ConcurrentDictionary`)** - fastest for unit tests and local Development loops; not durable and not shared across processes.

#### Architecture (implemented)
```
Platform.Storage          → canonical models + interfaces + in-memory impl
Platform.Storage.Relational → EF Core DbContext + SQLite/MySQL/PostgreSQL implementations
Platform.Hosting          → environment-aware DI registration helper
```

Interfaces (provider-neutral):
- `IMetricSampleStore` (incl. time-range `QueryAsync`)
- `IAggregateRollupStore`
- `IServiceCatalog`
- `IApiKeyStore`

#### Decision
**EF Core is the durable storage layer, supporting SQLite, MySQL and PostgreSQL as interchangeable providers**, encapsulated entirely behind `Platform.Storage` interfaces. EF types never leak outside `Platform.Storage.Relational`. All three share the same `PlatformDbContext`/entities; only the `UseSqlite`/`UseMySQL`/`UseNpgsql` call and the connection string differ.

This is fully agnostic to the services (e.g. OTLP producers) that push data into the ingestion metrics endpoint - they never see or configure a database provider.

In-memory remains the default for Development and Testing environments.

Registration (`Platform.Hosting.AddPlatformStorage`) reads `Storage:Provider` + `ConnectionStrings:Platform`:
- `Sqlite` (default durable choice) → `AddPlatformSqliteStorage`.
- `MySql` → `AddPlatformMySqlStorage` (MySql.EntityFrameworkCore).
- `Postgres` (or `PostgreSql`) → `AddPlatformPostgresStorage` (Npgsql.EntityFrameworkCore.PostgreSQL).
- `InMemory`, or no explicit provider while in Development/Testing → in-memory.
- Any other non-empty `Storage:Provider` value forces durable storage even in Development/Testing (useful for demos).

Example production config (same connection string on Ingestion + Api):
```json
{
  "Storage": { "Provider": "Sqlite" },
  "ConnectionStrings": {
    "Platform": "Data Source=/var/lib/platform/platform.db"
  }
}
```

MySQL:
```json
{
  "Storage": { "Provider": "MySql" },
  "ConnectionStrings": {
    "Platform": "Server=mysql-host;Database=platform;User=platform;Password=***;"
  }
}
```

PostgreSQL:
```json
{
  "Storage": { "Provider": "Postgres" },
  "ConnectionStrings": {
    "Platform": "Host=postgres-host;Database=platform;Username=platform;Password=***;"
  }
}
```

#### Current implementation status
- SQLite provider implemented and covered by conformance tests (write/read, restart persistence against the same file, tenant isolation, catalog, rollups, time-range, cancellation).
- Shared-file integration test: Ingestion writes samples; Api aggregates and reads rollups via separate hosts pointed at one SQLite file.
- MySQL and PostgreSQL providers are wired via the same `PlatformDbContext`/store implementations and selectable via `Storage:Provider`; they are not yet covered by conformance tests against real servers (no CI database service configured).
