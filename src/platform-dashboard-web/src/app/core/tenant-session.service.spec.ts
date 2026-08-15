import { TestBed } from '@angular/core/testing';

import { TenantSessionService } from './tenant-session.service';

describe('TenantSessionService', () => {
  let service: TenantSessionService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(TenantSessionService);
    service.signOut();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('should track which tenant the active session key belongs to', () => {
    service.signIn('t1', 'pk_abcd1234');

    expect(service.isAuthenticated).toBe(true);
    expect(service.isActiveTenant('t1')).toBe(true);
    expect(service.isActiveTenant('t2')).toBe(false);
  });

  it('should clear both the key and tenant on signOut', () => {
    service.signIn('t1', 'pk_abcd1234');
    service.signOut();

    expect(service.isAuthenticated).toBe(false);
    expect(service.isActiveTenant('t1')).toBe(false);
  });
});
