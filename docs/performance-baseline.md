### Performance baseline (Doc B MVP)

This is a **moderate-throughput MVP baseline**, not a hyperscale benchmark.
Doc B explicitly targets moderate MVP throughput, so no attempt was made to
over-optimize the ingestion or query paths beyond what already exists.
Numbers below are from a single local developer machine and are **not
representative of production infrastructure** - they exist only to prove
the platform is functional end-to-end under light load and to give a
rough, reproducible reference point for future comparisons.

#### Environment / caveats

- Measured on this dev machine (Windows, local `dotnet run -c Release`),
  single process per service, in-memory storage provider (no Sqlite/real DB),
  both services running locally with no network hop.
- No warm-up period was excluded from the numbers below (first-request JIT/
  connection-establishment cost is included), so real steady-state numbers
  after warm-up would be somewhat better.
- Client and server share the same machine, so results also include
  loopback/PowerShell `HttpClient` overhead, not just server-side cost.

#### How to reproduce

1. Build the solution: `dotnet build`
2. Start Platform.Ingestion in Development mode (seeds the deterministic dev
   API key and DevTenant) on port 5299:
   ```
   $env:ASPNETCORE_ENVIRONMENT="Development"
   dotnet run -c Release --project src/Platform.Ingestion --urls http://localhost:5299
   ```
3. Start Platform.Api the same way, on port 5298:
   ```
   $env:ASPNETCORE_ENVIRONMENT="Development"
   dotnet run -c Release --project src/Platform.Api --urls http://localhost:5298
   ```
4. Run the load script from the repo root:
   ```
   powershell -ExecutionPolicy Bypass -File benchmarks/run-ingestion-load.ps1 -RequestCount 300
   ```

The script (`benchmarks/run-ingestion-load.ps1`) is a **manual benchmark
tool only** - it is not part of `dotnet test` and is not referenced by any
test project. It sends N JSON requests to Platform.Ingestion's
`POST /v1/ingest` bridging endpoint (same storage/service-catalog write path
used by the OTLP `POST /v1/metrics` receiver), measuring wall-clock
throughput and per-request p50/p95 latency, then times one
`GET /v1/tenants/{tenantId}/services` call against Platform.Api.

#### Measured results (last run: 2026-08-14, 300 requests, in-memory storage)

| Metric | Value |
|---|---|
| Ingestion requests sent | 300 |
| Ingestion errors | 0 |
| Total wall-clock time | 10.78 s |
| Ingestion throughput | ~27.8 req/s |
| Ingestion p50 latency | 13.38 ms |
| Ingestion p95 latency | 62.35 ms |
| `GET /v1/tenants/{id}/services` latency (Platform.Api, single call, includes cold-start) | 105.06 ms |

These numbers comfortably exceed the moderate MVP throughput bar Doc B
targets (occasional bursts of tens of requests per second from a small
number of instrumented services) - they are not intended to demonstrate
hyperscale ingestion capacity.

#### Aggregation job duration (self-observability)

Aggregation run duration is emitted via the `Platform.Aggregation` Meter as
the histogram instrument `platform.aggregation.job.duration` (unit: `ms`),
added in a prior session (see `src/Platform.Aggregation/AggregationMetrics.cs`).
This instrument can be scraped via any OpenTelemetry Meter listener/exporter
attached to that Meter name; a live number was not captured in this baseline
run because the aggregation hosted service runs on a fixed interval
(default 1 minute) and this run was intentionally short. To capture a live
value, run the services for at least one aggregation interval and record
values reported for that histogram (e.g. via `dotnet-counters monitor -p <pid> Platform.Aggregation`).

#### Not covered / explicitly out of scope for this baseline

- Sustained/soak testing over long durations.
- Concurrent multi-client load (the script above is single-threaded/sequential).
- Sqlite or other durable storage provider throughput (only in-memory storage
  was exercised here).
- Production-grade infrastructure (load balancers, multiple instances, etc).
