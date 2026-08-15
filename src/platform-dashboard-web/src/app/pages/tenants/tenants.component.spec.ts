import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';

import { TenantsComponent } from './tenants.component';
import { TenantRegistryService } from '../../core/tenant-registry.service';
import { TenantSessionService } from '../../core/tenant-session.service';

describe('TenantsComponent', () => {
  let component: TenantsComponent;
  let fixture: ComponentFixture<TenantsComponent>;
  let registryMock: { tenants: () => any[]; add: jasmine.Spy };
  let sessionMock: { isActiveTenant: jasmine.Spy; signIn: jasmine.Spy };
  let dialogMock: { open: jasmine.Spy };
  let router: Router;

  beforeEach(async () => {
    registryMock = {
      tenants: () => [],
      add: jasmine.createSpy('add')
    };
    sessionMock = {
      isActiveTenant: jasmine.createSpy('isActiveTenant').and.returnValue(false),
      signIn: jasmine.createSpy('signIn')
    };
    dialogMock = {
      open: jasmine.createSpy('open')
    };

    await TestBed.configureTestingModule({
      imports: [TenantsComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        { provide: TenantRegistryService, useValue: registryMock },
        { provide: TenantSessionService, useValue: sessionMock },
        { provide: MatDialog, useValue: dialogMock }
      ]
    })
    .compileComponents();

    fixture = TestBed.createComponent(TenantsComponent);
    component = fixture.componentInstance;
    router = TestBed.inject(Router);
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should show empty state when there are no tenants', () => {
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('No tenants yet');
  });

  it('should register the tenant and activate its session key when the create dialog closes with a result', () => {
    const result = {
      tenantId: 't1',
      tenantName: 'Acme',
      apiKeyId: 'k1',
      apiKeyName: 'default',
      plaintextKey: 'pk_abcd1234',
      createdAt: '2024-01-01T00:00:00Z'
    };
    dialogMock.open.and.returnValue({ afterClosed: () => of(result) });

    component.openCreateDialog();

    expect(registryMock.add).toHaveBeenCalledWith(
      jasmine.objectContaining({ tenantId: 't1', tenantName: 'Acme' })
    );
    expect(sessionMock.signIn).toHaveBeenCalledWith('t1', 'pk_abcd1234');
  });

  it('should navigate directly to the tenant services view when managing a tenant with an active session key', () => {
    sessionMock.isActiveTenant.and.returnValue(true);
    const navigateSpy = spyOn(router, 'navigate');

    component.manage({
      tenantId: 't1',
      tenantName: 'Acme',
      apiKeyId: 'k1',
      apiKeyName: 'default',
      maskedKey: '••••••••••••1234',
      createdAt: '2024-01-01T00:00:00Z'
    });

    expect(dialogMock.open).not.toHaveBeenCalled();
    expect(navigateSpy).toHaveBeenCalledWith(['/tenants', 't1', 'services']);
  });

  it('should prompt for the API key when managing a tenant with no active session key', () => {
    sessionMock.isActiveTenant.and.returnValue(false);
    const navigateSpy = spyOn(router, 'navigate');
    dialogMock.open.and.returnValue({ afterClosed: () => of('pk_restored') });

    component.manage({
      tenantId: 't1',
      tenantName: 'Acme',
      apiKeyId: 'k1',
      apiKeyName: 'default',
      maskedKey: '••••••••••••1234',
      createdAt: '2024-01-01T00:00:00Z'
    });

    expect(dialogMock.open).toHaveBeenCalled();
    expect(navigateSpy).toHaveBeenCalledWith(['/tenants', 't1', 'services']);
  });

  it('should NOT navigate when the API key prompt is cancelled', () => {
    sessionMock.isActiveTenant.and.returnValue(false);
    const navigateSpy = spyOn(router, 'navigate');
    dialogMock.open.and.returnValue({ afterClosed: () => of(undefined) });

    component.manage({
      tenantId: 't1',
      tenantName: 'Acme',
      apiKeyId: 'k1',
      apiKeyName: 'default',
      maskedKey: '••••••••••••1234',
      createdAt: '2024-01-01T00:00:00Z'
    });

    expect(navigateSpy).not.toHaveBeenCalled();
  });
});
