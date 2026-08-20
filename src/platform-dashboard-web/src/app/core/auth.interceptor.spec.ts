import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { authInterceptor } from './auth.interceptor';
import { TenantSessionService } from './tenant-session.service';
import { environment } from '../../environments/environment';
import { AppConfigService } from './app-config.service';
import { AuthService } from './auth.service';

describe('authInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let session: TenantSessionService;
  let authMock: {
    authenticationEnabled: boolean;
    getAccessToken: jasmine.Spy;
  };

  beforeEach(() => {
    authMock = {
      authenticationEnabled: false,
      getAccessToken: jasmine.createSpy('getAccessToken').and.returnValue(null)
    };

    TestBed.configureTestingModule({
      providers: [
        AppConfigService,
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: authMock }
      ]
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    session = TestBed.inject(TenantSessionService);
    session.signOut();
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should NOT attach an X-Api-Key header when there is no active session (e.g. createTenant)', () => {
    http.post(`${environment.apiBaseUrl}/v1/tenants`, { name: 'Acme' }).subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/tenants`);
    expect(req.request.headers.has('X-Api-Key')).toBe(false);
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({});
  });

  it('should attach the X-Api-Key header once a session key is set (auth disabled)', () => {
    session.signIn('t1', 'pk_abcd1234');

    http.get(`${environment.apiBaseUrl}/v1/services`).subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/services`);
    expect(req.request.headers.get('X-Api-Key')).toBe('pk_abcd1234');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush([]);
  });

  it('should attach Bearer token when OIDC auth is enabled', () => {
    authMock.authenticationEnabled = true;
    authMock.getAccessToken.and.returnValue('oidc-access-token');

    http.get(`${environment.apiBaseUrl}/v1/tenants`).subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/tenants`);
    expect(req.request.headers.get('Authorization')).toBe('Bearer oidc-access-token');
    expect(req.request.headers.has('X-Api-Key')).toBe(false);
    req.flush([]);
  });

  it('should NOT attach Bearer token to non-API URLs when OIDC is enabled', () => {
    authMock.authenticationEnabled = true;
    authMock.getAccessToken.and.returnValue('oidc-access-token');

    http.get('https://login.example/.well-known/openid-configuration').subscribe();

    const req = httpMock.expectOne('https://login.example/.well-known/openid-configuration');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({});
  });

  it('should NOT attach credentials to the runtime config endpoint', () => {
    authMock.authenticationEnabled = true;
    authMock.getAccessToken.and.returnValue('oidc-access-token');
    session.signIn('t1', 'pk_abcd1234');

    http.get(`${environment.apiBaseUrl}/v1/platform/config`).subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/platform/config`);
    expect(req.request.headers.has('Authorization')).toBe(false);
    expect(req.request.headers.has('X-Api-Key')).toBe(false);
    req.flush({ apiBaseUrl: '', authentication: { enabled: true } });
  });
});
