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
| `/tenants/:tenantId/services/:id` | **Status: Done.** Per-service metrics dashboard with `1h` / `24h` / `7d` range toggle: summary cards (total executions, P50/P95/P99 or — when no executions, failure %, distinct request-type count), a throughput chart, a multi-line latency chart (P50/P95/P99), a failure-rate chart, and a server-side-paginated request-type ranking table. The full dashboard always renders, including for zero-traffic ranges. Same auth requirements as the services list. | `PlatformApiService.getServiceMetricsRaw(tenantId, id, range)` (cards/charts) + `PlatformApiService.getRequestTypeRanking(tenantId, id, range, page, pageSize)` (table). |

Loading, empty, and error states are handled for all HTTP calls.

### Service metrics dashboard
The per-service metrics page (`ServiceMetricsComponent`) consumes the raw rollup rows from
`GET /v1/tenants/{tenantId}/services/{id}/metrics?range=...` (via `PlatformApiService.getServiceMetricsRaw`) and
derives the summary cards and charts client-side through a pure transformation module,
`src/app/pages/service-metrics/metrics-view-model.ts` (`buildMetricsViewModel`), which is unit-tested in isolation
(`metrics-view-model.spec.ts`). The request-type ranking table is a separate, server-side-paginated data source (see
below) and is no longer computed by this module.

- **Failure rate** for a given `(requestType, bucket)` is `pipeline_exception.count` / `request.count` for that same
  requestType+bucket; a missing exception row is treated as zero exceptions (not "unknown").
- **Overall/per-bucket P50/P95/P99** are computed as a **count-weighted average** across the
  contributing `request.duration` rows (documented inline in `metrics-view-model.ts`). This is the simplest
  defensible aggregation that avoids "max of maxes" distortion while still weighting busier buckets/request types
  more heavily.
- Metrics with `requestType: null` (non-mediator-specific metrics) are excluded from the client-side charts described
  above; they are out of scope for this dashboard.
- **Correct temporal/idle semantics, never carry-forward**: `buildThroughput`/`buildLatency`/`buildFailureRate` in
  `metrics-view-model.ts` generate one entry per bucket across the entire selected range (from range start through
  to "now"), independent of whether that bucket has real data. Idle/no-execution buckets get throughput `0` and
  failure rate `0%` (valid, real values - not fabricated), while latency (P50/P95/P99) is `null` for idle buckets
  (a real gap - never 0ms and never the previous bucket's value). All three charts share the exact same bucket
  timeline/X-axis, and Chart.js is configured with `spanGaps: false` so `null` latency points render as visible
  breaks instead of being interpolated.
- **Known gap** (same as noted in the backend's own metrics documentation): the OSS mediator package does not emit
  a handler-type dimension, only `requestType` — so there is no handler-level ranking or drill-down here, only
  request-type. A request-type detail/drill-down view is intentionally out of scope for this iteration.

**Chart library**: [`ng2-charts`](https://github.com/valor-software/ng2-charts) (Chart.js wrapper), chosen over
`ngx-charts`/`ng-apexcharts` for its small footprint, trivial integration with standalone components via
`BaseChartDirective`/`provideCharts`, and because Chart.js exposes simple, explicit per-chart color/grid/tooltip
options that are easy to theme for this dark-mode-only UI (see `DARK_GRID_COLOR`/`DARK_TEXT_COLOR`/`DARK_TOOLTIP_BG`
in `service-metrics.component.ts`) without defaulting to light-mode-oriented styling.

**Status: Done.** Time-based X-axis (compact labels + fixed full-range domain). The throughput/latency/failure-rate
charts use Chart.js's `type: 'time'` scale (via `chartjs-adapter-date-fns`, which pulls in `date-fns` as its date
library) instead of a `category` scale, so the axis represents a genuine continuous time domain rather than shrinking
to fit only the data points present. Axis bounds/tick config are computed by the pure helper
`src/app/pages/service-metrics/chart-time-axis.ts` (unit-tested in `chart-time-axis.spec.ts`):

| Range | Axis `min`/`max` | `time.unit` | `stepSize` | Tick label format |
|---|---|---|---|---|
| `1h` | `now - 1h` .. `now` | `minute` | 10 | `HH:mm` (24h, e.g. `14:05`) |
| `24h` | `now - 24h` .. `now` | `hour` | 4 | `HH:mm` (24h, e.g. `14:00`) |
| `7d` | `now - 7d` .. `now` | `day` | 1 | short month + day (e.g. `Aug 10`) |

`now` is computed exactly once per data-load in `ServiceMetricsComponent.renderCharts` and shared across all three
charts, so they always share an identical axis domain instead of each chart independently calling `Date.now()`.
Ticks use `maxRotation: 0` and an explicit `maxTicksLimit` (in addition to `stepSize`) as a safety net against
overlapping labels. Tooltips always show the full exact timestamp (`formatTooltipTimestamp`, e.g.
`Aug 15, 2026, 14:23:07`) regardless of the axis tick compaction, plus the value(s) for that point.

**Status: Done.** Server-side-paginated request-types table. The request-type ranking table is populated from
`GET /v1/tenants/{tenantId}/services/{id}/request-types?range=1h|24h|7d&page=1&pageSize=20` via
`PlatformApiService.getRequestTypeRanking(tenantId, id, range, page, pageSize)`, independently of the
`/metrics`-driven summary cards/charts:
- **Default page size is 20**, with selectable page-size options **20 / 50 / 100** via a Material `mat-paginator`
  below the table (`length` bound to the response's `totalItems`).
- **Ordering is server-determined** (`executionCount` descending, `requestType` ascending tie-break); the client no
  longer sorts this table - the previous `matSort`-based client-side column sorting has been removed since it would
  otherwise silently mis-order an already-paginated slice of data.
- Changing the page or page size only re-fetches and re-renders the table (its own loading spinner/error state),
  without re-fetching or re-rendering the cards/charts.
- Changing the `1h`/`24h`/`7d` range resets the table back to page 1 and re-fetches both the `/metrics` data and the
  `/request-types` data.
- The table's loading/empty/error states are independent from the rest of the page: e.g. if `/request-types` fails
  while `/metrics` succeeds, the cards/charts render normally and only the table area shows an error state (and
  vice versa).
- The previous client-side `buildRanking`/`RequestTypeRankingRow` in `metrics-view-model.ts` has been removed, since
  the table's data source is now this paginated endpoint rather than a client-side aggregation of `/metrics` rows.

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
