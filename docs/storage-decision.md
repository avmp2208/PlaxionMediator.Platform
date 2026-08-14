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
Platform.Storage.Sqlite   → EF Core DbContext + SQLite implementations only
Platform.Hosting          → environment-aware DI registration helper
```

Interfaces (provider-neutral):
- `IMetricSampleStore` (incl. time-range `QueryAsync`)
- `IAggregateRollupStore`
- `IServiceCatalog`
- `IApiKeyStore`

#### Decision
**SQLite via EF Core is the MVP reference durable provider**, encapsulated entirely behind `Platform.Storage` interfaces. EF types never leak outside `Platform.Storage.Sqlite`.

In-memory remains the default for Development and Testing environments.

Registration (`Platform.Hosting.AddPlatformStorage`):
- Uses **SQLite** when `Storage:Provider=Sqlite`, or when environment is not Development/Testing and `ConnectionStrings:Platform` is set.
- Uses **InMemory** when `Storage:Provider=InMemory`, or in Development/Testing by default.

Example production config (same connection string on Ingestion + Api):
```json
{
  "Storage": { "Provider": "Sqlite" },
  "ConnectionStrings": {
    "Platform": "Data Source=/var/lib/platform/platform.db"
  }
}
```

#### Current implementation status
- SQLite provider implemented and covered by conformance tests (write/read, restart persistence against the same file, tenant isolation, catalog, rollups, time-range, cancellation).
- Shared-file integration test: Ingestion writes samples; Api aggregates and reads rollups via separate hosts pointed at one SQLite file.
- Postgres remains a future provider swap (new project implementing the same interfaces), not started.
