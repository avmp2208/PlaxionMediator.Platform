import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { of, throwError } from 'rxjs';

import { RebindKeyDialogComponent } from './rebind-key-dialog.component';
import { PlatformApiService } from '../../../core/platform-api.service';
import { TenantSessionService } from '../../../core/tenant-session.service';

describe('RebindKeyDialogComponent', () => {
  let component: RebindKeyDialogComponent;
  let fixture: ComponentFixture<RebindKeyDialogComponent>;
  let apiMock: { getServices: jasmine.Spy };
  let sessionMock: { signIn: jasmine.Spy; signOut: jasmine.Spy };
  let dialogRefMock: { close: jasmine.Spy };

  beforeEach(async () => {
    apiMock = {
      getServices: jasmine.createSpy('getServices')
    };
    sessionMock = {
      signIn: jasmine.createSpy('signIn'),
      signOut: jasmine.createSpy('signOut')
    };
    dialogRefMock = {
      close: jasmine.createSpy('close')
    };

    await TestBed.configureTestingModule({
      imports: [RebindKeyDialogComponent],
      providers: [
        provideNoopAnimations(),
        { provide: PlatformApiService, useValue: apiMock },
        { provide: TenantSessionService, useValue: sessionMock },
        { provide: MatDialogRef, useValue: dialogRefMock },
        { provide: MAT_DIALOG_DATA, useValue: { tenantId: 't1', tenantName: 'Acme' } }
      ]
    })
    .compileComponents();

    fixture = TestBed.createComponent(RebindKeyDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should show an error when submitting without an API key', () => {
    component.apiKey = '   ';
    component.submit();

    expect(component.error).toContain('Enter the API key');
    expect(apiMock.getServices).not.toHaveBeenCalled();
  });

  it('should activate and validate the entered key, closing with it on success', () => {
    apiMock.getServices.and.returnValue(of([]));

    component.apiKey = 'pk_restored';
    component.submit();

    expect(sessionMock.signIn).toHaveBeenCalledWith('t1', 'pk_restored');
    expect(apiMock.getServices).toHaveBeenCalledWith('t1');
    expect(dialogRefMock.close).toHaveBeenCalledWith('pk_restored');
    expect(component.loading).toBe(false);
  });

  it('should sign out and show an error when the entered key is rejected', () => {
    apiMock.getServices.and.returnValue(throwError(() => new Error('unauthorized')));

    component.apiKey = 'pk_bad';
    component.submit();

    expect(sessionMock.signOut).toHaveBeenCalled();
    expect(component.error).toContain('not accepted');
    expect(dialogRefMock.close).not.toHaveBeenCalled();
  });

  it('should close with undefined when cancelled', () => {
    component.cancel();
    expect(dialogRefMock.close).toHaveBeenCalledWith(undefined);
  });
});
