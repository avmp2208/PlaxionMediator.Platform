import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';

export interface AuthenticationConfig {
  enabled: boolean;
  authority?: string | null;
  clientId?: string | null;
  scopes?: string[];
  redirectPath?: string;
  postLogoutRedirectPath?: string;
}

export interface AppConfig {
  apiBaseUrl?: string;
  authentication?: AuthenticationConfig;
}

const DEFAULT_SCOPES = ['openid', 'profile', 'email'];

@Injectable({ providedIn: 'root' })
export class AppConfigService {
  private config: AppConfig = {};
  private configLoaded = false;

  constructor(private readonly http: HttpClient) {}

  /**
   * Loads public runtime config before the app boots.
   * Prefer GET {apiBaseUrl}/v1/platform/config (works same-origin and for ng serve),
   * then fall back to assets/config.json (container entrypoint), then environment.
   */
  async loadConfig(): Promise<void> {
    const fromApi = await this.tryLoadFromApi(environment.apiBaseUrl);
    if (fromApi) {
      this.config = fromApi;
      this.configLoaded = true;
      return;
    }

    const fromAssets = await this.tryLoadFromAssets();
    if (fromAssets) {
      this.config = fromAssets;
      this.configLoaded = true;

      // If assets only had apiBaseUrl (legacy), try the API once more with the resolved base.
      if (!fromAssets.authentication) {
        const enriched = await this.tryLoadFromApi(this.apiBaseUrl);
        if (enriched?.authentication) {
          this.config = {
            ...this.config,
            authentication: enriched.authentication,
            // Keep assets apiBaseUrl when it was explicitly set (including empty same-origin).
            apiBaseUrl: this.config.apiBaseUrl !== undefined ? this.config.apiBaseUrl : enriched.apiBaseUrl
          };
        }
      }
      return;
    }

    this.config = {};
    this.configLoaded = false;
  }

  get apiBaseUrl(): string {
    // An explicitly present (even empty-string) apiBaseUrl in runtime config is a deliberate
    // "same-origin" instruction from the container entrypoint and must NOT fall back to the
    // compiled environment default (which may point at an unreachable placeholder host).
    // Only fall back when config couldn't be loaded at all or omits the key entirely.
    if (this.configLoaded && this.config.apiBaseUrl !== undefined) {
      return this.config.apiBaseUrl;
    }
    return environment.apiBaseUrl;
  }

  get authenticationEnabled(): boolean {
    return !!this.config.authentication?.enabled;
  }

  get authentication(): AuthenticationConfig {
    const auth = this.config.authentication;
    return {
      enabled: !!auth?.enabled,
      authority: auth?.authority ?? null,
      clientId: auth?.clientId ?? null,
      scopes: auth?.scopes?.length ? auth.scopes : DEFAULT_SCOPES,
      redirectPath: auth?.redirectPath || '/auth/callback',
      postLogoutRedirectPath: auth?.postLogoutRedirectPath || '/'
    };
  }

  private async tryLoadFromApi(baseUrl: string | undefined): Promise<AppConfig | null> {
    try {
      const root = baseUrl ?? '';
      const url = `${root}/v1/platform/config`;
      const data = await firstValueFrom(this.http.get<AppConfig>(url));
      return data || null;
    } catch {
      return null;
    }
  }

  private async tryLoadFromAssets(): Promise<AppConfig | null> {
    try {
      const data = await firstValueFrom(this.http.get<AppConfig>('assets/config.json'));
      return data || null;
    } catch {
      return null;
    }
  }
}
