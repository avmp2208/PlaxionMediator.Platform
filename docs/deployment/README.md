# Deployment readiness status

**Status date:** 2026-08-17  
**Branch context:** Backend Deployment Readiness (post Doc B MVP)  
**Verdict:** See final report in the PR/issue — intended gate: **GO FOR PLATFORM CONTAINERIZATION** once `dotnet test` is green on this branch.

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

## Explicitly out of scope (next phase)

- [ ] Dockerfiles for Api / Ingestion / Dashboard
- [ ] Docker Compose (Api + Ingestion + Postgres + Dashboard)
- [ ] Real PostgreSQL integration validation in CI
- [ ] Container registry push
- [ ] Helm / Kubernetes manifests
- [ ] Staging environment provisioning
- [ ] CI/CD deploy pipelines
- [ ] MySQL EF migrations assembly (EnsureCreated only today)
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
