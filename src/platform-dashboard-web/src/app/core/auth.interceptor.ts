import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { TenantSessionService } from './tenant-session.service';
import { AppConfigService } from './app-config.service';
import { AuthService } from './auth.service';

/**
 * Attaches credentials to Platform.Api calls:
 * - When OIDC is enabled: Authorization: Bearer <access_token>
 * - When OIDC is disabled (local/dev): X-Api-Key from the legacy tenant session
 *
 * Never attaches either credential to non-API URLs (e.g. IdP, assets, telemetry).
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const config = inject(AppConfigService);
  const auth = inject(AuthService);
  const session = inject(TenantSessionService);

  // Skip attaching credentials to runtime-config bootstrap and static assets.
  if (req.url.includes('/v1/platform/config') || req.url.includes('assets/config.json')) {
    return next(req);
  }

  if (!isPlatformApiRequest(req.url, config.apiBaseUrl)) {
    return next(req);
  }

  if (auth.authenticationEnabled) {
    const token = auth.getAccessToken();
    if (token) {
      req = req.clone({
        setHeaders: {
          Authorization: `Bearer ${token}`
        }
      });
    }
    return next(req);
  }

  // Anonymous / auth-disabled mode: preserve existing API-key session behavior.
  const apiKey = session.apiKey();
  if (apiKey) {
    req = req.clone({
      setHeaders: {
        'X-Api-Key': apiKey
      }
    });
  }

  return next(req);
};

function isPlatformApiRequest(url: string, apiBaseUrl: string): boolean {
  // Same-origin (empty apiBaseUrl): treat absolute-path API calls as Platform.Api.
  if (!apiBaseUrl) {
    return url.startsWith('/v1/') || url.includes('/v1/');
  }

  return url.startsWith(apiBaseUrl);
}
