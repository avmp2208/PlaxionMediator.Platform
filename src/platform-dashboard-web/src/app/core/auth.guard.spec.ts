import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, convertToParamMap, provideRouter, Router, RouterStateSnapshot, UrlTree } from '@angular/router';

import { authGuard } from './auth.guard';
import { TenantSessionService } from './tenant-session.service';
import { AuthService } from './auth.service';

describe('authGuard', () => {
  let sessionMock: { isActiveTenant: jasmine.Spy };
  let authMock: {
    authenticationEnabled: boolean;
    hasValidAccessToken: jasmine.Spy;
    login: jasmine.Spy;
  };
  let router: Router;

  beforeEach(() => {
    sessionMock = {
      isActiveTenant: jasmine.createSpy('isActiveTenant')
    };
    authMock = {
      authenticationEnabled: false,
      hasValidAccessToken: jasmine.createSpy('hasValidAccessToken').and.returnValue(false),
      login: jasmine.createSpy('login')
    };

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: TenantSessionService, useValue: sessionMock },
        { provide: AuthService, useValue: authMock }
      ]
    });

    router = TestBed.inject(Router);
  });

  function routeWithTenantId(tenantId: string | null): ActivatedRouteSnapshot {
    return {
      paramMap: convertToParamMap(tenantId ? { tenantId } : {})
    } as ActivatedRouteSnapshot;
  }

  function stateWithUrl(url: string): RouterStateSnapshot {
    return { url } as RouterStateSnapshot;
  }

  it('allows activation when the session key belongs to the requested tenant (auth disabled)', () => {
    sessionMock.isActiveTenant.and.returnValue(true);

    const result = TestBed.runInInjectionContext(() =>
      authGuard(routeWithTenantId('t1'), stateWithUrl('/tenants/t1/services'))
    );

    expect(result).toBe(true);
  });

  it('redirects to / when there is no active session key for the requested tenant (auth disabled)', () => {
    sessionMock.isActiveTenant.and.returnValue(false);

    const result = TestBed.runInInjectionContext(() =>
      authGuard(routeWithTenantId('t1'), stateWithUrl('/tenants/t1/services'))
    ) as UrlTree;

    expect(result instanceof UrlTree).toBe(true);
    expect(router.serializeUrl(result)).toBe('/');
  });

  it('redirects to / when the route has no tenantId (auth disabled)', () => {
    const result = TestBed.runInInjectionContext(() =>
      authGuard(routeWithTenantId(null), stateWithUrl('/'))
    ) as UrlTree;

    expect(result instanceof UrlTree).toBe(true);
  });

  it('allows activation when OIDC has a valid access token', () => {
    authMock.authenticationEnabled = true;
    authMock.hasValidAccessToken.and.returnValue(true);

    const result = TestBed.runInInjectionContext(() =>
      authGuard(routeWithTenantId('t1'), stateWithUrl('/tenants/t1/services'))
    );

    expect(result).toBe(true);
    expect(authMock.login).not.toHaveBeenCalled();
  });

  it('starts OIDC login and blocks navigation when unauthenticated', () => {
    authMock.authenticationEnabled = true;
    authMock.hasValidAccessToken.and.returnValue(false);

    const result = TestBed.runInInjectionContext(() =>
      authGuard(routeWithTenantId('t1'), stateWithUrl('/tenants/t1/services'))
    );

    expect(result).toBe(false);
    expect(authMock.login).toHaveBeenCalledWith('/tenants/t1/services');
  });
});
