import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, convertToParamMap, provideRouter, Router, UrlTree } from '@angular/router';

import { authGuard } from './auth.guard';
import { TenantSessionService } from './tenant-session.service';

describe('authGuard', () => {
  let sessionMock: { isActiveTenant: jasmine.Spy };
  let router: Router;

  beforeEach(() => {
    sessionMock = {
      isActiveTenant: jasmine.createSpy('isActiveTenant')
    };

    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: TenantSessionService, useValue: sessionMock }]
    });

    router = TestBed.inject(Router);
  });

  function routeWithTenantId(tenantId: string | null): ActivatedRouteSnapshot {
    return {
      paramMap: convertToParamMap(tenantId ? { tenantId } : {})
    } as ActivatedRouteSnapshot;
  }

  it('allows activation when the session key belongs to the requested tenant', () => {
    sessionMock.isActiveTenant.and.returnValue(true);

    const result = TestBed.runInInjectionContext(() =>
      authGuard(routeWithTenantId('t1'), {} as any)
    );

    expect(result).toBe(true);
  });

  it('redirects to / when there is no active session key for the requested tenant', () => {
    sessionMock.isActiveTenant.and.returnValue(false);

    const result = TestBed.runInInjectionContext(() =>
      authGuard(routeWithTenantId('t1'), {} as any)
    ) as UrlTree;

    expect(result instanceof UrlTree).toBe(true);
    expect(router.serializeUrl(result)).toBe('/');
  });

  it('redirects to / when the route has no tenantId', () => {
    const result = TestBed.runInInjectionContext(() =>
      authGuard(routeWithTenantId(null), {} as any)
    ) as UrlTree;

    expect(result instanceof UrlTree).toBe(true);
  });
});
