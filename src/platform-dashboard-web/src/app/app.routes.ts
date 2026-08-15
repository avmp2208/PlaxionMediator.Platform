import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () => import('./pages/tenants/tenants.component').then(m => m.TenantsComponent)
  },
  {
    path: '',
    loadComponent: () => import('./layout/shell/shell.component').then(m => m.ShellComponent),
    // No authGuard: viewing a tenant's services only requires knowing its
    // TenantId (see ServicesController/ServiceMetricsController in
    // Platform.Api) - authentication/authorization is deferred to future
    // hardening work, out of scope for this MVP flow.
    children: [
      {
        path: 'tenants/:tenantId/services',
        loadComponent: () => import('./pages/services/services.component').then(m => m.ServicesComponent)
      },
      {
        path: 'tenants/:tenantId/services/:id',
        loadComponent: () =>
          import('./pages/service-metrics/service-metrics.component').then(m => m.ServiceMetricsComponent)
      }
    ]
  },
  { path: '**', redirectTo: '' }
];
