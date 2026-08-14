# Platform Dashboard (Angular)

**Status: DONE — Wired to Platform.Api.**

The Dashboard is a standalone **Angular (v18) + Angular Material** single-page application. It is wired to the `Platform.Api` backend for all data and authentication.

## Architecture
- **Environment-based configuration**: uses Angular's file-replacement system to point to different `apiBaseUrl` targets (development, staging, qa, production).
- **HTTP Interceptor**: a functional interceptor attaches the `X-Api-Key` header to all outgoing requests to the configured `apiBaseUrl`.
- **Tenant Isolation**: identity is resolved exclusively from the API key on the backend; the dashboard never passes a `TenantId`.

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
- Tenant identity is never entered as a GUID; the operator pastes an **API key**.
- The key is kept in **browser sessionStorage** (tab-scoped) and displayed only in masked form (e.g. `••••••••••••ab12`) in the UI; it is never logged.

## Pages
| Route | Purpose | Data |
|---|---|---|
| `/login` | Enter tenant API key. | Validated against `GET /v1/services`. |
| `/services` | Service list for the tenant. | `PlatformApiService.getServices()`. |
| `/services/:id` | Per-service metrics with `1h` / `24h` / `7d` range toggle, per-metric tables (avg/p50/p95/p99/min/max/count). | `PlatformApiService.getServiceMetrics()`. |

Loading, empty, and error states are handled for all HTTP calls.

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
