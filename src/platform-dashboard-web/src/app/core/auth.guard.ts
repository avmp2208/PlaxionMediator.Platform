import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { TenantSessionService } from './tenant-session.service';

/**
 * Guards the services/metrics routes. These endpoints are now protected
 * server-side (see ServicesController/ServiceMetricsController in
 * Platform.Api) and require the tenant's own API key to be presented as
 * X-Api-Key - the TenantId route segment alone is never trusted. This
 * guard redirects home when there is no active session key for the
 * requested tenant, so the user is prompted to (re)supply the key via the
 * "Manage" flow instead of hitting a 401/404 from the API.
 */
export const authGuard: CanActivateFn = (route) => {
  const session = inject(TenantSessionService);
  const router = inject(Router);

  const tenantId = route.paramMap.get('tenantId');
  if (tenantId && session.isActiveTenant(tenantId)) {
    return true;
  }

  return router.createUrlTree(['/']);
};
