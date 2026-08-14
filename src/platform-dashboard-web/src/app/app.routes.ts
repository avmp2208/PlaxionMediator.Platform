import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./pages/login/login.component').then(m => m.LoginComponent)
  },
  {
    path: '',
    loadComponent: () => import('./layout/shell/shell.component').then(m => m.ShellComponent),
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'services' },
      {
        path: 'services',
        loadComponent: () => import('./pages/services/services.component').then(m => m.ServicesComponent)
      },
      {
        path: 'services/:id',
        loadComponent: () =>
          import('./pages/service-metrics/service-metrics.component').then(m => m.ServiceMetricsComponent)
      }
    ]
  },
  { path: '**', redirectTo: 'services' }
];
