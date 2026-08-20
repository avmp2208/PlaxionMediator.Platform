import { TestBed } from '@angular/core/testing';
import { OAuthService } from 'angular-oauth2-oidc';
import { AuthService } from './auth.service';
import { AppConfigService, AuthenticationConfig } from './app-config.service';

describe('AuthService', () => {
  let service: AuthService;
  let oauthMock: jasmine.SpyObj<OAuthService>;
  let configMock: {
    authenticationEnabled: boolean;
    authentication: AuthenticationConfig;
  };

  beforeEach(() => {
    oauthMock = jasmine.createSpyObj<OAuthService>('OAuthService', [
      'configure',
      'setStorage',
      'loadDiscoveryDocumentAndTryLogin',
      'setupAutomaticSilentRefresh',
      'initCodeFlow',
      'logOut',
      'getAccessToken',
      'hasValidAccessToken'
    ]);
    (oauthMock as any).events = {
      subscribe: () => ({ unsubscribe() {} })
    };
    (oauthMock as any).state = '';
    oauthMock.loadDiscoveryDocumentAndTryLogin.and.resolveTo(true);
    oauthMock.hasValidAccessToken.and.returnValue(false);
    oauthMock.getAccessToken.and.returnValue('');

    configMock = {
      authenticationEnabled: false,
      authentication: {
        enabled: false,
        authority: null,
        clientId: null,
        scopes: ['openid', 'profile', 'email'],
        redirectPath: '/auth/callback',
        postLogoutRedirectPath: '/'
      }
    };

    TestBed.configureTestingModule({
      providers: [
        AuthService,
        { provide: OAuthService, useValue: oauthMock },
        { provide: AppConfigService, useValue: configMock }
      ]
    });

    service = TestBed.inject(AuthService);
  });

  it('skips OIDC configuration when authentication is disabled', async () => {
    await service.init();

    expect(oauthMock.configure).not.toHaveBeenCalled();
    expect(oauthMock.loadDiscoveryDocumentAndTryLogin).not.toHaveBeenCalled();
    expect(service.isAuthenticated).toBe(true);
    expect(service.getAccessToken()).toBeNull();
  });

  it('configures Authorization Code + PKCE when authentication is enabled', async () => {
    configMock.authenticationEnabled = true;
    configMock.authentication = {
      enabled: true,
      authority: 'https://login.example/',
      clientId: 'platform-dashboard',
      scopes: ['openid', 'profile', 'email'],
      redirectPath: '/auth/callback',
      postLogoutRedirectPath: '/'
    };
    oauthMock.hasValidAccessToken.and.returnValue(true);
    oauthMock.getAccessToken.and.returnValue('access-token-1');

    await service.init();

    expect(oauthMock.configure).toHaveBeenCalled();
    const cfg = oauthMock.configure.calls.mostRecent().args[0] as any;
    expect(cfg.responseType).toBe('code');
    expect(cfg.clientId).toBe('platform-dashboard');
    expect(cfg.issuer).toBe('https://login.example/');
    expect(cfg.scope).toContain('openid');
    expect(oauthMock.setStorage).toHaveBeenCalledWith(sessionStorage);
    expect(oauthMock.loadDiscoveryDocumentAndTryLogin).toHaveBeenCalled();
    expect(service.hasValidAccessToken()).toBe(true);
    expect(service.getAccessToken()).toBe('access-token-1');
  });

  it('login is a no-op when authentication is disabled', () => {
    service.login('/tenants/x/services');
    expect(oauthMock.initCodeFlow).not.toHaveBeenCalled();
  });

  it('login starts the code flow when authentication is enabled', () => {
    configMock.authenticationEnabled = true;
    service.login('/tenants/x/services');
    expect(oauthMock.initCodeFlow).toHaveBeenCalledWith('/tenants/x/services');
  });

  it('logout clears OIDC state via the library when enabled', () => {
    configMock.authenticationEnabled = true;
    service.logout();
    expect(oauthMock.logOut).toHaveBeenCalled();
  });

  it('logout is a no-op when authentication is disabled', () => {
    service.logout();
    expect(oauthMock.logOut).not.toHaveBeenCalled();
  });
});
