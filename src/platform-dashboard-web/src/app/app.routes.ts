import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () => import('./pages/tenants/tenants.component').then(m => m.TenantsComponent)
  },
  {
    path: '',
    loadComponent: () => import('./layout/shell/shell.component').then(m => m.ShellComponent),
    // Services/metrics are gated server-side by [Authorize] +
    // TenantId-from-key validation (see ServicesController/
    // ServiceMetricsController in Platform.Api). authGuard mirrors that
    // requirement client-side: it redirects to '/' when there is no active
    // session key for the requested tenant, so the user is prompted to
    // supply the key instead of hitting a 401/404 from the API.
    children: [
      {
        path: 'tenants/:tenantId/services',
        canActivate: [authGuard],
        loadComponent: () => import('./pages/services/services.component').then(m => m.ServicesComponent)
      },
      {
        path: 'tenants/:tenantId/services/:id',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./pages/service-metrics/service-metrics.component').then(m => m.ServiceMetricsComponent)
      }
    ]
  },
  { path: '**', redirectTo: '' }
];
