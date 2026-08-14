import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { TenantSessionService } from './tenant-session.service';
import { environment } from '../../environments/environment';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const session = inject(TenantSessionService);
  const apiKey = session.apiKey();

  // Only attach header to requests targeting our configured Platform.Api
  if (apiKey && req.url.startsWith(environment.apiBaseUrl)) {
    req = req.clone({
      setHeaders: {
        'X-Api-Key': apiKey
      }
    });
  }

  return next(req);
};
