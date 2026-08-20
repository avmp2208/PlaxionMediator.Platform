import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';

import { TenantsComponent } from './tenants.component';
import { TenantRegistryService } from '../../core/tenant-registry.service';
import { TenantSessionService } from '../../core/tenant-session.service';
import { AuthService } from '../../core/auth.service';
import { PlatformApiService } from '../../core/platform-api.service';

describe('TenantsComponent', () => {
  let component: TenantsComponent;
  let fixture: ComponentFixture<TenantsComponent>;
  let registryMock: { tenants: () => any[]; add: jasmine.Spy };
  let sessionMock: { isActiveTenant: jasmine.Spy; signIn: jasmine.Spy };
  let authMock: {
    authenticationEnabled: boolean;
    hasValidAccessToken: jasmine.Spy;
    login: jasmine.Spy;
    logout: jasmine.Spy;
  };
  let apiMock: { getTenants: jasmine.Spy };
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
    authMock = {
      authenticationEnabled: false,
      hasValidAccessToken: jasmine.createSpy('hasValidAccessToken').and.returnValue(false),
      login: jasmine.createSpy('login'),
      logout: jasmine.createSpy('logout')
    };
    apiMock = {
      getTenants: jasmine.createSpy('getTenants').and.returnValue(of([]))
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
        { provide: AuthService, useValue: authMock },
        { provide: PlatformApiService, useValue: apiMock },
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

  it('should register the tenant and activate its session key when auth is disabled', () => {
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

  it('should not use the API key as session credential when OIDC is enabled', () => {
    // Rebuild component in OIDC mode.
    authMock.authenticationEnabled = true;
    authMock.hasValidAccessToken.and.returnValue(true);
    fixture = TestBed.createComponent(TenantsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

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

    expect(registryMock.add).toHaveBeenCalled();
    expect(sessionMock.signIn).not.toHaveBeenCalled();
    expect(apiMock.getTenants).toHaveBeenCalled();
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

  it('should prompt for the API key when managing a tenant with no active session key (auth disabled)', () => {
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

  it('should open tenant services without API-key prompt when OIDC is enabled', () => {
    authMock.authenticationEnabled = true;
    authMock.hasValidAccessToken.and.returnValue(true);
    fixture = TestBed.createComponent(TenantsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    const navigateSpy = spyOn(router, 'navigate');
    component.manage({
      tenantId: 't-oidc',
      role: 'Owner',
      createdAt: '2024-01-01T00:00:00Z'
    });

    expect(dialogMock.open).not.toHaveBeenCalled();
    expect(navigateSpy).toHaveBeenCalledWith(['/tenants', 't-oidc', 'services']);
  });

  it('shows Invitations nav for Owners and hides it for Members', () => {
    authMock.authenticationEnabled = true;
    authMock.hasValidAccessToken.and.returnValue(true);
    apiMock.getTenants.and.returnValue(
      of([{ tenantId: 't-owner', role: 'Owner', createdAt: '2024-01-01T00:00:00Z' }])
    );

    fixture = TestBed.createComponent(TenantsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    expect(component.canManageInvitations()).toBeTrue();
    let el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="invitations-nav"]')).not.toBeNull();
    expect(el.textContent).toContain('Invitations');

    apiMock.getTenants.and.returnValue(
      of([{ tenantId: 't-member', role: 'Member', createdAt: '2024-01-01T00:00:00Z' }])
    );
    fixture = TestBed.createComponent(TenantsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    expect(component.canManageInvitations()).toBeFalse();
    el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="invitations-nav"]')).toBeNull();
  });

  it('loads newly claimed tenants on fresh GET /v1/tenants after login (JIT claim, no accept UI)', () => {
    authMock.authenticationEnabled = true;
    authMock.hasValidAccessToken.and.returnValue(true);
    apiMock.getTenants.and.returnValue(
      of([
        { tenantId: 'claimed-1', role: 'Member', createdAt: '2024-06-01T00:00:00Z' },
        { tenantId: 'owned-1', role: 'Owner', createdAt: '2024-01-01T00:00:00Z' }
      ])
    );

    fixture = TestBed.createComponent(TenantsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    expect(apiMock.getTenants).toHaveBeenCalled();
    expect(component.serverTenants().map((t) => t.tenantId)).toEqual([
      'claimed-1',
      'owned-1'
    ]);
    expect(component.serverTenants().find((t) => t.tenantId === 'claimed-1')?.role).toBe('Member');

    const text = (fixture.nativeElement as HTMLElement).textContent || '';
    expect(text.toLowerCase()).not.toContain('accept invitation');
    // Member still sees Open action for services.
    expect(text).toContain('Open');
  });

  it('reloads tenants from the server on explicit refresh (no client cache of memberships)', () => {
    authMock.authenticationEnabled = true;
    authMock.hasValidAccessToken.and.returnValue(true);
    apiMock.getTenants.and.returnValue(of([]));
    fixture = TestBed.createComponent(TenantsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    const callsAfterInit = apiMock.getTenants.calls.count();

    apiMock.getTenants.and.returnValue(
      of([{ tenantId: 'new-claim', role: 'Member', createdAt: '2024-07-01T00:00:00Z' }])
    );
    component.reloadServerTenants();
    fixture.detectChanges();

    expect(apiMock.getTenants.calls.count()).toBeGreaterThan(callsAfterInit);
    expect(component.serverTenants()[0].tenantId).toBe('new-claim');
  });
});
