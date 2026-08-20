import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';
import { AppConfigService } from './app-config.service';

/**
 * Maps Platform.Api 401/403 responses to a simple unauthorized UI route when
 * OIDC auth is enabled. Does not interfere with anonymous/API-key mode.
 */
export const authErrorInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const config = inject(AppConfigService);
  const router = inject(Router);

  return next(req).pipe(
    catchError((err: unknown) => {
      if (
        auth.authenticationEnabled &&
        err instanceof HttpErrorResponse &&
        (err.status === 401 || err.status === 403) &&
        isPlatformApiRequest(req.url, config.apiBaseUrl)
      ) {
        void router.navigate(['/unauthorized'], {
          queryParams: { status: err.status }
        });
      }
      return throwError(() => err);
    })
  );
};

function isPlatformApiRequest(url: string, apiBaseUrl: string): boolean {
  if (!apiBaseUrl) {
    return url.startsWith('/v1/') || url.includes('/v1/');
  }
  return url.startsWith(apiBaseUrl);
}
