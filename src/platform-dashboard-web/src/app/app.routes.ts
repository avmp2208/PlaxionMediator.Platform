import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () => import('./pages/tenants/tenants.component').then(m => m.TenantsComponent)
  },
  {
    path: 'auth/callback',
    loadComponent: () =>
      import('./pages/auth-callback/auth-callback.component').then(m => m.AuthCallbackComponent)
  },
  {
    path: 'unauthorized',
    loadComponent: () =>
      import('./pages/unauthorized/unauthorized.component').then(m => m.UnauthorizedComponent)
  },
  {
    path: '',
    loadComponent: () => import('./layout/shell/shell.component').then(m => m.ShellComponent),
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
