import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { authInterceptor } from './auth.interceptor';
import { TenantSessionService } from './tenant-session.service';
import { environment } from '../../environments/environment';

describe('authInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let session: TenantSessionService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting()
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
    req.flush({});
  });

  it('should attach the X-Api-Key header once a session key is set', () => {
    session.signIn('t1', 'pk_abcd1234');

    http.get(`${environment.apiBaseUrl}/v1/services`).subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/services`);
    expect(req.request.headers.get('X-Api-Key')).toBe('pk_abcd1234');
    req.flush([]);
  });
});
