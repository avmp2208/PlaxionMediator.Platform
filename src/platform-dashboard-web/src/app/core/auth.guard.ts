import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { TenantSessionService } from './tenant-session.service';

export const authGuard: CanActivateFn = () => {
  const session = inject(TenantSessionService);
  const router = inject(Router);

  if (session.isAuthenticated) {
    return true;
  }

  return router.createUrlTree(['/login']);
};
