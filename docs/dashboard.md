# Platform Dashboard (Angular)

**Status: Done (MVP) — Wired to Platform.Api; tenant-list-first flow with direct "Manage" navigation (no API key re-entry).**

The Dashboard is a standalone **Angular (v18) + Angular Material** single-page application. It is wired to the `Platform.Api` backend for all data and authentication.

## Architecture
- **Environment-based configuration**: uses Angular's file-replacement system to point to different `apiBaseUrl` targets (development, staging, qa, production).
- **HTTP Interceptor**: a functional interceptor attaches the `X-Api-Key` header to all outgoing requests to the configured `apiBaseUrl`.
- **Tenant scoping**: services/metrics endpoints take `TenantId` directly in the URL (no API key required for this MVP stage); key-management endpoints still resolve the tenant exclusively from a validated API key.

## Project location
```
src/platform-dashboard-web/   Angular CLI workspace (standalone components)
```

## Environment Configuration
Environment files are located in `src/environments/`:
- `environment.ts`: Default/Development (points to `http://localhost:5095`).
- `environment.staging.ts`: Staging.
- `environment.qa.ts`: QA.
- `environment.prod.ts`: Production.

To add a new environment:
1. Create `src/environments/environment.<name>.ts`.
2. Add the configuration to `angular.json` under `projects.platform-dashboard-web.architect.build.configurations` and `serve.configurations`.

## Principles
- Consumes **Platform.Api only via HTTP** — no direct storage access.
- **Viewing** a tenant's services/metrics only requires knowing its `TenantId` (carried in the URL, e.g. `/tenants/:tenantId/services`) — no API key is required for this MVP stage. Real access control (RBAC or similar) is deferred to future hardening work and is explicitly out of scope for now.
- The tenant API key remains required only for **key-management** actions (`GET/POST /v1/api-keys`, `DELETE /v1/api-keys/{id}`) and for OTLP ingestion from the customer's application — never for browsing the Dashboard.
- A separate **local tenant registry** (`TenantRegistryService`, backed by `localStorage`) remembers, per browser, which tenants the user created on this device (name, masked key, created date, TenantId) so the tenant list survives browser restarts. It never stores the raw plaintext API key — only a masked form for display.

## Pages
| Route | Purpose | Data |
|---|---|---|
| `/` | **Landing page.** Lists all tenants created on this browser (`TenantRegistryService`), with a "Create Tenant" button and a "Manage" action per tenant. Empty state when no tenants exist yet. | `TenantRegistryService.tenants()` (local, no HTTP). |
| `/login` | Fallback entry point: paste an existing tenant API key directly (also linked from `/` as "Already have an API key?"). Resolves the key's `TenantId` via `GET /v1/api-keys/whoami`, then navigates into the tenant. | `PlatformApiService.whoAmI()`. |
| `/tenants/:tenantId/services` | Service list for the given tenant (reached via "Manage" on `/`, or via `/login`). No API key required. | `PlatformApiService.getServices(tenantId)`. |
| `/tenants/:tenantId/services/:id` | Per-service metrics with `1h` / `24h` / `7d` range toggle, per-metric tables (avg/p50/p95/p99/min/max/count). No API key required. | `PlatformApiService.getServiceMetrics(tenantId, id)`. |

Loading, empty, and error states are handled for all HTTP calls.

### Tenant-list-first flow
The user lands on `/` first and sees the list of tenants (each meaning one API key/pk they created on this browser):
1. **Create Tenant** opens a dialog asking for a tenant/organization name. It calls `PlatformApiService.createTenant(name)` (`POST /v1/tenants`, no auth header required). On success, the returned `plaintextKey` is shown **once** in a copyable field (`@angular/cdk/clipboard`, with a "Copied!" snackbar) with a one-time warning that it will not be shown again — this key is meant to be handed to the *service* that will ingest metrics, not re-entered into the Dashboard. The dialog then adds a masked entry (including the `TenantId`) to the local tenant registry.
2. Clicking **Manage** on a tenant navigates **directly** to `/tenants/:tenantId/services` — no API key prompt. Viewing a tenant's services/metrics is intentionally not gated by authentication in this MVP; real access control (RBAC or similar) is deferred to future hardening work, tracked separately from this MVP scope.
3. From the shell, an **"All Tenants"** link in the sidenav returns to `/` so the user can switch to or manage another tenant.

Backend endpoints this flow depends on: `POST /v1/tenants` (no auth), `GET /v1/tenants/{tenantId}/services` (no auth), `GET /v1/tenants/{tenantId}/services/{id}/metrics` (no auth), `GET/POST /v1/api-keys`, `DELETE /v1/api-keys/{id}`, `GET /v1/api-keys/whoami` (these four require `X-Api-Key`).

## Theme
Dark-mode-only Angular Material theme (`theme-type: dark`) defined in `src/styles.scss`.

## Run locally
```powershell
cd src/platform-dashboard-web
npm install   # first time only
ng serve      # http://localhost:4200 (Development)
```

## CORS
`Platform.Api` must be configured to allow the Dashboard's origin. In `appsettings.json`, add the origin to `Cors:AllowedOrigins`:
```json
"Cors": {
  "AllowedOrigins": [ "http://localhost:4200" ]
}
```

## Tests
```powershell
cd src/platform-dashboard-web
npm test -- --watch=false --browsers=ChromeHeadless
```
