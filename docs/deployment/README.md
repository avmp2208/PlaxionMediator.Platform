# Deployment readiness status

**Status date:** 2026-11-25 (originally 2026-08-17; refreshed — the "next phase" list below had fallen out of date with what actually shipped)
**Branch context:** Backend Deployment Readiness (post Doc B MVP)
**Verdict:** **Containerization and Kubernetes packaging are done.** What's left is proving the whole chain end-to-end on a fresh cluster — see the [plaxion-platform repo](https://github.com/avmp2208/plaxion-platform)'s `docs/installation.md` and the rehearsal step in the platform launch plan.

## Done in this task

- [x] Configuration audit + strongly-typed options (`Platform`, `Storage`, `Ingestion`, `Aggregation`, `Retention`, `Cors`, `Security`, `Observability`, `Database`) with `ValidateOnStart`
- [x] Environment layering: `appsettings.json` → `appsettings.{Environment}.json` → env vars
- [x] `appsettings.Production.json` / `appsettings.Staging.json` templates (no secrets) for Api + Ingestion
- [x] Secrets audit: no embedded production credentials; connection strings / OTLP headers via env
- [x] Storage provider boundary preserved (EF types stay in `Platform.Storage.Relational`)
- [x] PostgreSQL registration verified (`UseNpgsql`); provider selection config-driven
- [x] EF Core **Initial** migrations for **Sqlite** and **PostgreSQL** (Doc B schema)
- [x] Migration execution policy: Dev auto-migrate; Production explicit `--migrate`; no replica race by default
- [x] Migration safety tests (create schema, preserve data, history table)
- [x] No dev/seeded API key anywhere - self-service `POST /v1/tenants` provisions tenant + key in every environment
- [x] `InMemory`/`Sqlite` restricted to local Development/Testing - Staging/QA/Production require PostgreSql or MySql
- [x] Startup fail-fast for missing CS / unsupported provider / invalid intervals
- [x] Liveness (`/health`, `/healthz`) vs readiness (`/health/ready` + DB check)
- [x] CORS configuration-driven (Api only)
- [x] Non-secret startup diagnostics log line
- [x] Graceful shutdown: background services already honor `CancellationToken` via `PeriodicTimer` / `BackgroundService`
- [x] Container contract: removed absolute user-profile paths from `launchSettings`; configurable dev key path
- [x] Docs: this folder
- [x] **Dockerfile** — single multi-role image (`ROLE=api` serves API + embedded Angular dashboard SPA; `ROLE=ingestion` serves OTLP/HTTP; same image also runs EF migrations via `ROLE=api --migrate`). See `Dockerfile`, `docker/entrypoint.sh`.
- [x] **Docker Compose** — local multi-service stack. See `docker-compose.yml`.
- [x] **Container registry push** — `.github/workflows/release.yml` logs into GHCR and publishes the image on `v*.*.*` tag push.
- [x] **Real PostgreSQL integration validation in CI** — `.github/workflows/ci.yml` runs a live Postgres service; 232/232 backend tests green against it, including dedicated PostgreSQL conformance tests.
- [x] **Helm / Kubernetes manifests** — maintained in the separate [`plaxion-platform`](https://github.com/avmp2208/plaxion-platform) repo (chart `v0.3.0`: Deployments, Services, Ingress + Traefik IngressRoute, HPA, NetworkPolicy, migration Job hook, 8 example deployment profiles). Not yet published to a public registry/OCI artifact — see that repo's release checklist.

## Still out of scope

- [ ] **Staging environment provisioning** — no persistent staging cluster exists yet.
- [ ] **A real end-to-end release rehearsal** — nobody has run the Helm chart on a genuinely fresh cluster with real external Postgres, real OIDC, and a real app sending real telemetry. Everything above has been verified piecemeal (unit/integration tests, chart lint/template), not as one continuous flow.
- [ ] MySQL EF migrations assembly (`EnsureCreated` only today; MySQL is also not exposed by the Helm chart, which only supports PostgreSQL)
- [ ] Doc C/D/E/F product features and CLI product work

## Required production settings (checklist)

| Setting | Example env var |
|---------|-----------------|
| Environment | `ASPNETCORE_ENVIRONMENT=Production` |
| Storage provider | `Storage__Provider=PostgreSql` |
| Connection string | `ConnectionStrings__Platform=Host=...;Database=platform;...` |
| CORS origins (Api) | `Cors__AllowedOrigins__0=https://app.example.com` |
| URLs | `ASPNETCORE_URLS=http://+:8080` |
| Migrate once | `dotnet Platform.Api.dll --migrate` |

## Related docs

- [backend-configuration.md](./backend-configuration.md) — full key reference
- [database-migrations.md](./database-migrations.md) — migration strategy and `--migrate`
- [../local-dev-setup.md](../local-dev-setup.md) — local developer workflow
