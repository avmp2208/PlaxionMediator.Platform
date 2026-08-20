import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';

import { InvitationsComponent } from './invitations.component';
import { AuthService } from '../../core/auth.service';
import { PlatformApiService } from '../../core/platform-api.service';
import { TenantRegistryService } from '../../core/tenant-registry.service';
import { InvitationItem, TenantListItem } from '../../core/models';

describe('InvitationsComponent', () => {
  let fixture: ComponentFixture<InvitationsComponent>;
  let component: InvitationsComponent;
  let apiMock: {
    getTenants: jasmine.Spy;
    getInvitations: jasmine.Spy;
    createInvitations: jasmine.Spy;
    revokeInvitation: jasmine.Spy;
  };
  let authMock: {
    authenticationEnabled: boolean;
    hasValidAccessToken: jasmine.Spy;
    login: jasmine.Spy;
  };

  const ownerTenants: TenantListItem[] = [
    {
      tenantId: '11111111-1111-1111-1111-111111111111',
      role: 'Owner',
      createdAt: '2024-01-01T00:00:00Z',
      tenantName: 'Acme'
    },
    {
      tenantId: '22222222-2222-2222-2222-222222222222',
      role: 'Owner',
      createdAt: '2024-01-02T00:00:00Z',
      tenantName: 'Beta'
    },
    {
      tenantId: '33333333-3333-3333-3333-333333333333',
      role: 'Member',
      createdAt: '2024-01-03T00:00:00Z',
      tenantName: 'Gamma'
    }
  ];

  const pendingInvite: InvitationItem = {
    id: 'inv-pending',
    tenantId: '11111111-1111-1111-1111-111111111111',
    email: 'new@example.com',
    status: 'Pending',
    createdAt: '2024-02-01T00:00:00Z',
    claimedAt: null
  };

  const claimedInvite: InvitationItem = {
    id: 'inv-claimed',
    tenantId: '11111111-1111-1111-1111-111111111111',
    email: 'old@example.com',
    status: 'Claimed',
    createdAt: '2024-01-15T00:00:00Z',
    claimedAt: '2024-01-16T00:00:00Z'
  };

  const revokedInvite: InvitationItem = {
    id: 'inv-revoked',
    tenantId: '22222222-2222-2222-2222-222222222222',
    email: 'gone@example.com',
    status: 'Revoked',
    createdAt: '2024-01-10T00:00:00Z',
    claimedAt: null
  };

  async function setup(options?: {
    tenants?: TenantListItem[];
    invitationsByTenant?: Record<string, InvitationItem[]>;
  }): Promise<void> {
    TestBed.resetTestingModule();

    const tenants = options?.tenants ?? ownerTenants;
    const invitationsByTenant = options?.invitationsByTenant ?? {
      '11111111-1111-1111-1111-111111111111': [pendingInvite, claimedInvite],
      '22222222-2222-2222-2222-222222222222': [revokedInvite]
    };

    apiMock = {
      getTenants: jasmine.createSpy('getTenants').and.returnValue(of(tenants)),
      getInvitations: jasmine
        .createSpy('getInvitations')
        .and.callFake((tenantId: string) => of(invitationsByTenant[tenantId] ?? [])),
      createInvitations: jasmine.createSpy('createInvitations').and.returnValue(
        of({ invitations: [pendingInvite], skippedTenantIds: [] })
      ),
      revokeInvitation: jasmine.createSpy('revokeInvitation').and.returnValue(
        of({ ...pendingInvite, status: 'Revoked' })
      )
    };

    authMock = {
      authenticationEnabled: true,
      hasValidAccessToken: jasmine.createSpy('hasValidAccessToken').and.returnValue(true),
      login: jasmine.createSpy('login')
    };

    await TestBed.configureTestingModule({
      imports: [InvitationsComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        { provide: PlatformApiService, useValue: apiMock },
        { provide: AuthService, useValue: authMock },
        {
          provide: TenantRegistryService,
          useValue: { tenants: () => [] }
        }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(InvitationsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('shows invitation management UI for Owners', async () => {
    await setup();
    const text = (fixture.nativeElement as HTMLElement).textContent || '';
    expect(text).toContain('Invite by email');
    expect(text).toContain('Send invitation');
    expect(component.canAccessInvitations()).toBeTrue();
    expect(component.ownedTenants().length).toBe(2);
  });

  it('hides invite form for Member-only users', async () => {
    await setup({
      tenants: [
        {
          tenantId: '33333333-3333-3333-3333-333333333333',
          role: 'Member',
          createdAt: '2024-01-03T00:00:00Z'
        }
      ]
    });
    const text = (fixture.nativeElement as HTMLElement).textContent || '';
    expect(text).toContain('available to tenant owners only');
    expect(text).not.toContain('Send invitation');
    expect(component.canAccessInvitations()).toBeFalse();
  });

  it('lets an Owner select multiple owned tenants in the invite form', async () => {
    await setup();
    // Default selects all owned tenants.
    expect(component.selectedTenantIds.size).toBe(2);
    component.clearTenantSelection();
    component.toggleTenant('11111111-1111-1111-1111-111111111111', true);
    component.toggleTenant('22222222-2222-2222-2222-222222222222', true);
    expect(component.isTenantSelected('11111111-1111-1111-1111-111111111111')).toBeTrue();
    expect(component.isTenantSelected('22222222-2222-2222-2222-222222222222')).toBeTrue();
    // Member tenant must not appear in owned list.
    expect(
      component.ownedTenants().some((t) => t.tenantId === '33333333-3333-3333-3333-333333333333')
    ).toBeFalse();
  });

  it('sends correct email + tenantIds payload on invite submit', async () => {
    await setup();
    component.email = '  invitee@example.com ';
    component.selectedTenantIds = new Set([
      '11111111-1111-1111-1111-111111111111',
      '22222222-2222-2222-2222-222222222222'
    ]);

    component.submitInvite();

    expect(apiMock.createInvitations).toHaveBeenCalled();
    const [email, tenantIds] = apiMock.createInvitations.calls.mostRecent().args as [string, string[]];
    expect(email).toBe('invitee@example.com');
    expect(tenantIds).toEqual(
      jasmine.arrayWithExactContents([
        '11111111-1111-1111-1111-111111111111',
        '22222222-2222-2222-2222-222222222222'
      ])
    );
  });

  it('validates email client-side before calling the API', async () => {
    await setup();
    component.email = 'not-an-email';
    component.submitInvite();
    expect(apiMock.createInvitations).not.toHaveBeenCalled();
    expect(component.formError()).toContain('valid email');
  });

  it('requires at least one selected tenant', async () => {
    await setup();
    component.email = 'ok@example.com';
    component.clearTenantSelection();
    component.submitInvite();
    expect(apiMock.createInvitations).not.toHaveBeenCalled();
    expect(component.formError()).toContain('Select at least one tenant');
  });

  it('renders Pending/Claimed/Revoked with revoke only on Pending', async () => {
    await setup();
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent || '';
    expect(text).toContain('Pending');
    expect(text).toContain('Claimed');
    expect(text).toContain('Revoked');
    expect(text).toContain('new@example.com');

    expect(component.canRevoke(pendingInvite)).toBeTrue();
    expect(component.canRevoke(claimedInvite)).toBeFalse();
    expect(component.canRevoke(revokedInvite)).toBeFalse();

    const revokeButtons = (fixture.nativeElement as HTMLElement).querySelectorAll(
      'button[color="warn"], button.mat-warn, td button'
    );
    const revokeLabels = Array.from(revokeButtons)
      .map((b) => (b.textContent || '').trim())
      .filter((t) => t === 'Revoke');
    expect(revokeLabels.length).toBe(1);
  });

  it('revokes a Pending invitation and updates status', async () => {
    await setup();
    component.revoke(pendingInvite);
    expect(apiMock.revokeInvitation).toHaveBeenCalledWith('inv-pending');
    expect(component.invitations().find((i) => i.id === 'inv-pending')?.status).toBe('Revoked');
    expect(component.formSuccess()).toContain('revoked');
  });

  it('surfaces backend validation/duplicate errors as user-facing messages', async () => {
    await setup();
    apiMock.createInvitations.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { error: 'A pending invitation already exists for this tenant and email.' }
          })
      )
    );
    component.email = 'dup@example.com';
    component.selectedTenantIds = new Set(['11111111-1111-1111-1111-111111111111']);
    component.submitInvite();
    expect(component.formError()).toContain('pending invitation already exists');
  });

  it('does not include any manual accept-invitation flow', async () => {
    await setup();
    const text = (fixture.nativeElement as HTMLElement).textContent || '';
    expect(text.toLowerCase()).not.toContain('accept invitation');
    expect(text.toLowerCase()).toContain('automatically');
  });
});
