import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { userFacingApiError } from '../../core/api-error';
import { AuthService } from '../../core/auth.service';
import { InvitationItem, TenantListItem } from '../../core/models';
import { PlatformApiService } from '../../core/platform-api.service';
import { TenantRegistryService } from '../../core/tenant-registry.service';
import {
  anyMembershipHasCapability,
  canManageInvitations
} from '../../core/tenant-capabilities';

/** Row shown in the invitations table, with optional display name. */
export interface InvitationRow extends InvitationItem {
  tenantName?: string;
}

@Component({
  selector: 'app-invitations',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatTableModule,
    MatTooltipModule
  ],
  templateUrl: './invitations.component.html',
  styleUrl: './invitations.component.scss'
})
export class InvitationsComponent implements OnInit {
  readonly displayedColumns = ['email', 'tenant', 'status', 'createdAt', 'actions'];

  readonly loading = signal(true);
  readonly loadError = signal('');
  readonly submitting = signal(false);
  readonly formError = signal('');
  readonly formSuccess = signal('');
  readonly revokingId = signal<string | null>(null);

  /** All memberships from GET /v1/tenants (server roles only). */
  readonly memberships = signal<TenantListItem[]>([]);
  readonly invitations = signal<InvitationRow[]>([]);

  /** Tenants the caller owns — only source for invite multi-select. */
  readonly ownedTenants = computed(() =>
    this.memberships().filter((t) => canManageInvitations(t.role))
  );

  readonly canAccessInvitations = computed(() =>
    anyMembershipHasCapability(this.memberships(), 'ManageInvitations')
  );

  email = '';
  /** Selected owned tenant ids for the invite form. */
  selectedTenantIds = new Set<string>();

  constructor(
    private readonly api: PlatformApiService,
    private readonly auth: AuthService,
    private readonly registry: TenantRegistryService,
    private readonly router: Router
  ) {}

  ngOnInit(): void {
    if (!this.auth.authenticationEnabled) {
      this.loading.set(false);
      this.loadError.set('Invitations require sign-in. Enable authentication to manage members.');
      return;
    }
    if (!this.auth.hasValidAccessToken()) {
      this.auth.login('/invitations');
      return;
    }
    this.reload();
  }

  reload(): void {
    this.loading.set(true);
    this.loadError.set('');
    // Fresh GET /v1/tenants — server JIT-claims pending invites for this user first.
    this.api.getTenants().subscribe({
      next: (tenants) => {
        const nameById = this.buildNameMap();
        const enriched = tenants.map((t) => ({
          ...t,
          tenantName: nameById.get(t.tenantId.toLowerCase())
        }));
        this.memberships.set(enriched);

        const owned = enriched.filter((t) => canManageInvitations(t.role));
        if (owned.length === 0) {
          this.invitations.set([]);
          this.loading.set(false);
          return;
        }

        // Default-select all owned tenants on first load if nothing chosen yet.
        if (this.selectedTenantIds.size === 0) {
          this.selectedTenantIds = new Set(owned.map((t) => t.tenantId));
        }

        this.loadInvitationsForOwned(owned, nameById);
      },
      error: (err) => {
        this.loading.set(false);
        this.loadError.set(userFacingApiError(err, 'Unable to load tenants.'));
      }
    });
  }

  isTenantSelected(tenantId: string): boolean {
    return this.selectedTenantIds.has(tenantId);
  }

  toggleTenant(tenantId: string, checked: boolean): void {
    const next = new Set(this.selectedTenantIds);
    if (checked) {
      next.add(tenantId);
    } else {
      next.delete(tenantId);
    }
    this.selectedTenantIds = next;
  }

  selectAllOwned(): void {
    this.selectedTenantIds = new Set(this.ownedTenants().map((t) => t.tenantId));
  }

  clearTenantSelection(): void {
    this.selectedTenantIds = new Set();
  }

  submitInvite(): void {
    this.formError.set('');
    this.formSuccess.set('');

    const email = this.email.trim();
    if (!email) {
      this.formError.set('Enter an email address.');
      return;
    }
    if (!isValidEmail(email)) {
      this.formError.set('Enter a valid email address.');
      return;
    }

    const tenantIds = Array.from(this.selectedTenantIds);
    if (tenantIds.length === 0) {
      this.formError.set('Select at least one tenant you own.');
      return;
    }

    // Guard: only owned tenants may be submitted (never trust stale UI state).
    const ownedIds = new Set(this.ownedTenants().map((t) => t.tenantId));
    const invalid = tenantIds.filter((id) => !ownedIds.has(id));
    if (invalid.length > 0) {
      this.formError.set('One or more selected tenants are not owned by you. Refresh and try again.');
      return;
    }

    this.submitting.set(true);
    this.api.createInvitations(email, tenantIds).subscribe({
      next: (response) => {
        this.submitting.set(false);
        const created = response.invitations?.length ?? 0;
        const skipped = response.skippedTenantIds?.length ?? 0;
        if (created === 0 && skipped > 0) {
          this.formSuccess.set(
            'No new invitations were created — the invitee is already a member of the selected tenant(s).'
          );
        } else if (skipped > 0) {
          this.formSuccess.set(
            `Created ${created} invitation(s). ${skipped} tenant(s) skipped (already a member).`
          );
        } else {
          this.formSuccess.set(`Created ${created} invitation(s).`);
        }
        this.email = '';
        this.refreshInvitationList();
      },
      error: (err) => {
        this.submitting.set(false);
        this.formError.set(userFacingApiError(err, 'Unable to create invitation.'));
      }
    });
  }

  canRevoke(row: InvitationRow): boolean {
    return (row.status || '').toLowerCase() === 'pending';
  }

  revoke(row: InvitationRow): void {
    if (!this.canRevoke(row)) {
      return;
    }
    this.formError.set('');
    this.formSuccess.set('');
    this.revokingId.set(row.id);
    this.api.revokeInvitation(row.id).subscribe({
      next: (updated) => {
        this.revokingId.set(null);
        this.invitations.update((list) =>
          list.map((item) =>
            item.id === updated.id
              ? {
                  ...item,
                  status: updated.status,
                  claimedAt: updated.claimedAt
                }
              : item
          )
        );
        this.formSuccess.set('Invitation revoked.');
      },
      error: (err) => {
        this.revokingId.set(null);
        this.formError.set(userFacingApiError(err, 'Unable to revoke invitation.'));
      }
    });
  }

  displayTenantName(tenantId: string, tenantName?: string): string {
    if (tenantName) {
      return tenantName;
    }
    const fromMembership = this.memberships().find(
      (t) => t.tenantId.toLowerCase() === tenantId.toLowerCase()
    );
    if (fromMembership?.tenantName) {
      return fromMembership.tenantName;
    }
    return tenantId.length > 8 ? `${tenantId.slice(0, 8)}…` : tenantId;
  }

  statusClass(status: string): string {
    switch ((status || '').toLowerCase()) {
      case 'pending':
        return 'status-pending';
      case 'claimed':
        return 'status-claimed';
      case 'revoked':
        return 'status-revoked';
      default:
        return '';
    }
  }

  goBack(): void {
    this.router.navigateByUrl('/');
  }

  private refreshInvitationList(): void {
    const owned = this.ownedTenants();
    if (owned.length === 0) {
      this.invitations.set([]);
      return;
    }
    this.loadInvitationsForOwned(owned, this.buildNameMap());
  }

  private loadInvitationsForOwned(
    owned: TenantListItem[],
    nameById: Map<string, string>
  ): void {
    const requests = owned.map((t) =>
      this.api.getInvitations(t.tenantId).pipe(
        catchError(() => of([] as InvitationItem[]))
      )
    );

    forkJoin(requests).subscribe({
      next: (lists) => {
        const rows: InvitationRow[] = [];
        lists.forEach((list, index) => {
          const tenant = owned[index];
          for (const inv of list) {
            rows.push({
              ...inv,
              tenantName:
                nameById.get(inv.tenantId.toLowerCase()) ?? tenant.tenantName
            });
          }
        });
        rows.sort((a, b) => b.createdAt.localeCompare(a.createdAt));
        this.invitations.set(rows);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.loadError.set(userFacingApiError(err, 'Unable to load invitations.'));
      }
    });
  }

  private buildNameMap(): Map<string, string> {
    return new Map(
      this.registry.tenants().map((t) => [t.tenantId.toLowerCase(), t.tenantName])
    );
  }
}

function isValidEmail(value: string): boolean {
  // Practical client-side check; server also normalizes and validates.
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value);
}
