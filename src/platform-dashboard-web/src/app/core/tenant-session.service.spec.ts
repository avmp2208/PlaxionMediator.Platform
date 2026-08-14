import { TestBed } from '@angular/core/testing';

import { TenantSessionService } from './tenant-session.service';

describe('TenantSessionService', () => {
  let service: TenantSessionService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(TenantSessionService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
