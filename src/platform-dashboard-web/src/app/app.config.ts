import { APP_INITIALIZER, ApplicationConfig, provideZoneChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideCharts, withDefaultRegisterables } from 'ng2-charts';
import { provideOAuthClient } from 'angular-oauth2-oidc';

import { routes } from './app.routes';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { authInterceptor } from './core/auth.interceptor';
import { authErrorInterceptor } from './core/auth-error.interceptor';
import { AppConfigService } from './core/app-config.service';
import { AuthService } from './core/auth.service';

function initializeApp(config: AppConfigService, auth: AuthService): () => Promise<void> {
  return async () => {
    await config.loadConfig();
    await auth.init();
  };
}

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes),
    provideAnimationsAsync(),
    provideHttpClient(withInterceptors([authInterceptor, authErrorInterceptor])),
    provideCharts(withDefaultRegisterables()),
    provideOAuthClient(),
    {
      provide: APP_INITIALIZER,
      useFactory: initializeApp,
      deps: [AppConfigService, AuthService],
      multi: true
    }
  ]
};
