import { TestBed } from '@angular/core/testing';
import { TenantRegistryService } from './tenant-registry.service';
import { TenantRegistryEntry } from './models';

describe('TenantRegistryService', () => {
  let service: TenantRegistryService;

  beforeEach(() => {
    localStorage.removeItem('platform.tenantRegistry');
    TestBed.configureTestingModule({});
    service = TestBed.inject(TenantRegistryService);
  });

  afterEach(() => {
    localStorage.removeItem('platform.tenantRegistry');
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('should start with an empty tenants list', () => {
    expect(service.tenants()).toEqual([]);
  });

  it('should add a tenant entry and persist it to localStorage', () => {
    const entry: TenantRegistryEntry = {
      tenantId: 't1',
      tenantName: 'Acme Corp',
      apiKeyId: 'k1',
      apiKeyName: 'default',
      maskedKey: '••••••••••••ab12',
      createdAt: '2024-01-01T00:00:00Z'
    };

    service.add(entry);

    expect(service.tenants()).toEqual([entry]);

    const raw = localStorage.getItem('platform.tenantRegistry');
    expect(raw).not.toBeNull();
    expect(JSON.parse(raw!)).toEqual([entry]);
  });

  it('should load persisted tenants from localStorage on construction', () => {
    const entry: TenantRegistryEntry = {
      tenantId: 't2',
      tenantName: 'Beta Inc',
      apiKeyId: 'k2',
      apiKeyName: 'default',
      maskedKey: '••••••••••••cd34',
      createdAt: '2024-02-01T00:00:00Z'
    };
    localStorage.setItem('platform.tenantRegistry', JSON.stringify([entry]));

    const reloaded = new TenantRegistryService();
    expect(reloaded.tenants()).toEqual([entry]);
  });

  it('should mask an api key showing only the last 4 characters', () => {
    const masked = TenantRegistryService.mask('pk_1234567890abcd1234');
    expect(masked.endsWith('1234')).toBe(true);
    expect(masked).toContain('••••••••••••');
  });
});
