import { Injectable } from '@angular/core';
import { OAuthErrorEvent, OAuthService } from 'angular-oauth2-oidc';
import { BehaviorSubject } from 'rxjs';
import { AppConfigService } from './app-config.service';

/**
 * Wraps angular-oauth2-oidc for the Dashboard.
 * When runtime authentication.enabled is false, all OIDC calls are no-ops so
 * local ng serve / anonymous bootstrap keeps working.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly authenticatedSubject = new BehaviorSubject<boolean>(false);
  readonly isAuthenticated$ = this.authenticatedSubject.asObservable();

  private initialized = false;

  constructor(
    private readonly oauth: OAuthService,
    private readonly config: AppConfigService
  ) {
    this.oauth.events.subscribe((event) => {
      if (event instanceof OAuthErrorEvent) {
        // Keep local state honest when the library reports auth failures.
        this.authenticatedSubject.next(this.hasValidAccessToken());
      } else {
        this.authenticatedSubject.next(this.hasValidAccessToken());
      }
    });
  }

  get authenticationEnabled(): boolean {
    return this.config.authenticationEnabled;
  }

  get isAuthenticated(): boolean {
    if (!this.authenticationEnabled) {
      return true;
    }
    return this.hasValidAccessToken();
  }

  /**
   * Configure OIDC from runtime config and complete any redirect callback.
   * Must run after AppConfigService.loadConfig().
   */
  async init(): Promise<void> {
    if (this.initialized) {
      return;
    }
    this.initialized = true;

    if (!this.authenticationEnabled) {
      this.authenticatedSubject.next(true);
      return;
    }

    const auth = this.config.authentication;
    if (!auth.authority || !auth.clientId) {
      console.warn(
        'Authentication is enabled but authority/clientId are missing from runtime config. OIDC will not start.'
      );
      this.authenticatedSubject.next(false);
      return;
    }

    const redirectPath = auth.redirectPath || '/auth/callback';
    const postLogoutPath = auth.postLogoutRedirectPath || '/';
    const origin = window.location.origin;

    this.oauth.configure({
      issuer: auth.authority,
      clientId: auth.clientId,
      responseType: 'code',
      scope: (auth.scopes && auth.scopes.length ? auth.scopes : ['openid', 'profile', 'email']).join(' '),
      redirectUri: `${origin}${redirectPath.startsWith('/') ? redirectPath : '/' + redirectPath}`,
      postLogoutRedirectUri: `${origin}${postLogoutPath.startsWith('/') ? postLogoutPath : '/' + postLogoutPath}`,
      showDebugInformation: false,
      // Public SPA client: Authorization Code + PKCE, no client secret.
      oidc: true,
      requireHttps: auth.authority.startsWith('https://'),
      // sessionStorage keeps tokens out of long-lived localStorage.
      // The library default uses sessionStorage when useSilentRefresh is false
      // unless a custom storage is provided; we set clearHashAfterLogin for cleanliness.
      clearHashAfterLogin: true,
      strictDiscoveryDocumentValidation: false
    });

    // Prefer sessionStorage for tokens (tab-scoped).
    this.oauth.setStorage(sessionStorage);

    try {
      await this.oauth.loadDiscoveryDocumentAndTryLogin();
    } catch (err) {
      console.error('Failed to initialize OIDC discovery/login', err);
    }

    // Silent refresh when the IdP advertises a session iframe / refresh token.
    if (this.oauth.hasValidAccessToken()) {
      this.oauth.setupAutomaticSilentRefresh();
    }

    this.authenticatedSubject.next(this.hasValidAccessToken());
  }

  login(returnUrl?: string): void {
    if (!this.authenticationEnabled) {
      return;
    }
    const state = returnUrl || '/';
    this.oauth.initCodeFlow(state);
  }

  logout(): void {
    if (!this.authenticationEnabled) {
      return;
    }
    // Uses end_session_endpoint from discovery metadata when available.
    this.oauth.logOut();
    this.authenticatedSubject.next(false);
  }

  getAccessToken(): string | null {
    if (!this.authenticationEnabled) {
      return null;
    }
    const token = this.oauth.getAccessToken();
    return token || null;
  }

  hasValidAccessToken(): boolean {
    if (!this.authenticationEnabled) {
      return false;
    }
    return this.oauth.hasValidAccessToken();
  }

  /** State passed through the OIDC round-trip (return URL). */
  getReturnUrl(): string {
    const state = this.oauth.state;
    if (typeof state === 'string' && state.startsWith('/')) {
      return state;
    }
    return '/';
  }
}
