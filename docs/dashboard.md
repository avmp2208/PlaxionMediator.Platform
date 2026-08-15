# Platform Dashboard (Angular)

**Status: Done (MVP) — Wired to Platform.Api; tenant-list-first flow. "Manage" navigates directly for tenants created in the current browser tab (whose plaintext key is already in memory); tenants from a previous session require re-entering the API key.**

The Dashboard is a standalone **Angular (v18) + Angular Material** single-page application. It is wired to the `Platform.Api` backend for all data and authentication.

## Architecture
- **Environment-based configuration**: uses Angular's file-replacement system to point to different `apiBaseUrl` targets (development, staging, qa, production).
- **HTTP Interceptor**: a functional interceptor attaches the `X-Api-Key` header to all outgoing requests to the configured `apiBaseUrl`.
- **Tenant scoping**: services/metrics endpoints require a valid `X-Api-Key` header (`[Authorize]`); the `TenantId` route segment is never trusted on its own - it must match the TenantId resolved from the authenticated key, or the request is rejected with 404 (no key at all is rejected with 401). Key-management endpoints resolve the tenant exclusively from the validated API key the same way.

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
- **Viewing** a tenant's services/metrics requires presenting that tenant's own API key as `X-Api-Key` (carried by `TenantSessionService`/`authInterceptor`); the `TenantId` in the URL (e.g. `/tenants/:tenantId/services`) is never trusted on its own and is cross-checked against the authenticated tenant server-side. A client-side `authGuard` mirrors this and redirects to `/` when there is no active session key for the requested tenant.
- The tenant API key is required for **key-management** actions (`GET/POST /v1/api-keys`, `DELETE /v1/api-keys/{id}`), for OTLP ingestion from the customer's application, and for browsing the Dashboard's services/metrics pages.
- A separate **local tenant registry** (`TenantRegistryService`, backed by `localStorage`) remembers, per browser, which tenants the user created on this device (name, masked key, created date, TenantId) so the tenant list survives browser restarts. It never stores the raw plaintext API key — only a masked form for display.

## Pages
| Route | Purpose | Data |
|---|---|---|
| `/` | **Landing page.** Lists all tenants created on this browser (`TenantRegistryService`), with a "Create Tenant" button and a "Manage" action per tenant. Empty state when no tenants exist yet. | `TenantRegistryService.tenants()` (local, no HTTP). |
| `/login` | Fallback entry point: paste an existing tenant API key directly (also linked from `/` as "Already have an API key?"). Resolves the key's `TenantId` via `GET /v1/api-keys/whoami`, then navigates into the tenant. | `PlatformApiService.whoAmI()`. |
| `/tenants/:tenantId/services` | Service list for the given tenant (reached via "Manage" on `/`). Requires an active session API key for this tenant (`authGuard`); the server independently rejects any mismatch between the key's tenant and the URL's `TenantId`. | `PlatformApiService.getServices(tenantId)`. |
| `/tenants/:tenantId/services/:id` | Per-service metrics with `1h` / `24h` / `7d` range toggle, per-metric tables (avg/p50/p95/p99/min/max/count). Same auth requirements as the services list. | `PlatformApiService.getServiceMetrics(tenantId, id)`. |

Loading, empty, and error states are handled for all HTTP calls.

### Tenant-list-first flow
The user lands on `/` first and sees the list of tenants (each meaning one API key/pk they created on this browser):
1. **Create Tenant** opens a dialog asking for a tenant/organization name. It calls `PlatformApiService.createTenant(name)` (`POST /v1/tenants`, no auth header required). On success, the returned `plaintextKey` is shown **once** in a copyable field (`@angular/cdk/clipboard`, with a "Copied!" snackbar) with a one-time warning that it will not be shown again. The dialog adds a masked entry (including the `TenantId`) to the local tenant registry, and `TenantsComponent` immediately activates the plaintext key as the active `TenantSessionService` session for that tenant, since it is already in memory for this browser tab.
2. Clicking **Manage** on a tenant checks whether the active session key already belongs to that tenant (true right after creating it in the current tab): if so it navigates **directly** to `/tenants/:tenantId/services`. Otherwise (e.g. the tenant was created in a previous browser session/tab, so its plaintext key is no longer held in memory) a minimal `RebindKeyDialogComponent` prompts the user to paste the tenant's API key; the key is validated against `GET /v1/tenants/{tenantId}/services` before being activated and the user is navigated in only on success.
3. `authGuard` additionally guards the `services`/`services/:id` routes and redirects to `/` if there is no active session key for the requested tenant (e.g. direct URL navigation/refresh without going through "Manage").
4. From the shell, an **"All Tenants"** link in the sidenav returns to `/` so the user can switch to or manage another tenant.

Backend endpoints this flow depends on: `POST /v1/tenants` (no auth), `GET /v1/tenants/{tenantId}/services` (requires `X-Api-Key`; TenantId in the URL must match the key's tenant or a 404 is returned), `GET /v1/tenants/{tenantId}/services/{id}/metrics` (same), `GET/POST /v1/api-keys`, `DELETE /v1/api-keys/{id}`, `GET /v1/api-keys/whoami` (all require `X-Api-Key`).

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
