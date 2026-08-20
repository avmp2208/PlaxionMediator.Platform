import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { TenantSessionService } from './tenant-session.service';
import { AuthService } from './auth.service';

/**
 * Guards services/metrics routes.
 *
 * - OIDC enabled: require a valid access token; otherwise start login and cancel navigation.
 * - OIDC disabled: preserve the legacy per-tenant API-key session check so local
 *   anonymous bootstrap still works without an IdP.
 */
export const authGuard: CanActivateFn = (route, state) => {
  const auth = inject(AuthService);
  const session = inject(TenantSessionService);
  const router = inject(Router);

  if (auth.authenticationEnabled) {
    if (auth.hasValidAccessToken()) {
      return true;
    }
    auth.login(state.url);
    return false;
  }

  const tenantId = route.paramMap.get('tenantId');
  if (tenantId && session.isActiveTenant(tenantId)) {
    return true;
  }

  return router.createUrlTree(['/']);
};
