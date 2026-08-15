import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';

import { TenantsComponent } from './tenants.component';
import { TenantRegistryService } from '../../core/tenant-registry.service';

describe('TenantsComponent', () => {
  let component: TenantsComponent;
  let fixture: ComponentFixture<TenantsComponent>;
  let registryMock: { tenants: () => any[]; add: jasmine.Spy };
  let dialogMock: { open: jasmine.Spy };
  let router: Router;

  beforeEach(async () => {
    registryMock = {
      tenants: () => [],
      add: jasmine.createSpy('add')
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

  it('should register the tenant when the create dialog closes with a result', () => {
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
  });

  it('should navigate directly to the tenant services view when managing, with no auth prompt', () => {
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
});
