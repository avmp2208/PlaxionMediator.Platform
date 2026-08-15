import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MatDialogRef } from '@angular/material/dialog';
import { of, throwError } from 'rxjs';

import { CreateTenantDialogComponent } from './create-tenant-dialog.component';
import { PlatformApiService } from '../../../core/platform-api.service';

describe('CreateTenantDialogComponent', () => {
  let component: CreateTenantDialogComponent;
  let fixture: ComponentFixture<CreateTenantDialogComponent>;
  let apiMock: { createTenant: jasmine.Spy };
  let dialogRefMock: { close: jasmine.Spy };

  beforeEach(async () => {
    apiMock = {
      createTenant: jasmine.createSpy('createTenant')
    };
    dialogRefMock = {
      close: jasmine.createSpy('close')
    };

    await TestBed.configureTestingModule({
      imports: [CreateTenantDialogComponent],
      providers: [
        provideNoopAnimations(),
        { provide: PlatformApiService, useValue: apiMock },
        { provide: MatDialogRef, useValue: dialogRefMock }
      ]
    })
    .compileComponents();

    fixture = TestBed.createComponent(CreateTenantDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should show an error when submitting without a tenant name', () => {
    component.tenantName = '   ';
    component.submit();
    expect(component.error).toContain('Enter a tenant');
    expect(apiMock.createTenant).not.toHaveBeenCalled();
  });

  it('should store the create-tenant response on success', () => {
    const response = {
      tenantId: 't1',
      tenantName: 'Acme',
      apiKeyId: 'k1',
      apiKeyName: 'default',
      plaintextKey: 'pk_abcd1234',
      createdAt: '2024-01-01T00:00:00Z'
    };
    apiMock.createTenant.and.returnValue(of(response));

    component.tenantName = 'Acme';
    component.submit();

    expect(apiMock.createTenant).toHaveBeenCalledWith('Acme');
    expect(component.result).toEqual(response);
    expect(component.loading).toBe(false);
  });

  it('should surface an error message when creation fails', () => {
    apiMock.createTenant.and.returnValue(throwError(() => new Error('boom')));

    component.tenantName = 'Acme';
    component.submit();

    expect(component.error).toContain('Unable to create tenant');
    expect(component.loading).toBe(false);
  });

  it('should close the dialog with the result when done() is called', () => {
    const response = {
      tenantId: 't1',
      tenantName: 'Acme',
      apiKeyId: 'k1',
      apiKeyName: 'default',
      plaintextKey: 'pk_abcd1234',
      createdAt: '2024-01-01T00:00:00Z'
    };
    component.result = response;
    component.done();

    expect(dialogRefMock.close).toHaveBeenCalledWith(response);
  });
});
