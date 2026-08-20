import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TenantRegistryService } from '../../core/tenant-registry.service';
import { TenantSessionService } from '../../core/tenant-session.service';
import { AuthService } from '../../core/auth.service';
import { PlatformApiService } from '../../core/platform-api.service';
import { TenantListItem, TenantRegistryEntry } from '../../core/models';
import { CreateTenantDialogComponent } from './create-tenant-dialog/create-tenant-dialog.component';
import { RebindKeyDialogComponent } from './rebind-key-dialog/rebind-key-dialog.component';

@Component({
  selector: 'app-tenants',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatIconModule,
    MatTableModule,
    MatTooltipModule,
    MatProgressSpinnerModule
  ],
  templateUrl: './tenants.component.html',
  styleUrl: './tenants.component.scss'
})
export class TenantsComponent implements OnInit {
  readonly oidcMode: boolean;
  readonly displayedColumns: string[];

  /** OIDC mode: tenants from GET /v1/tenants. */
  readonly serverTenants = signal<TenantListItem[]>([]);
  readonly loading = signal(false);
  readonly loadError = signal('');

  constructor(
    readonly registry: TenantRegistryService,
    private readonly session: TenantSessionService,
    readonly auth: AuthService,
    private readonly api: PlatformApiService,
    private readonly dialog: MatDialog,
    private readonly router: Router
  ) {
    this.oidcMode = this.auth.authenticationEnabled;
    this.displayedColumns = this.oidcMode
      ? ['name', 'role', 'createdAt', 'actions']
      : ['name', 'maskedKey', 'createdAt', 'actions'];
  }

  ngOnInit(): void {
    if (this.oidcMode) {
      if (!this.auth.hasValidAccessToken()) {
        // Landing page still renders; prompt sign-in rather than auto-redirecting
        // so first-time visitors see context. Manage/create will trigger login via guard/API.
        return;
      }
      this.reloadServerTenants();
    }
  }

  get showEmpty(): boolean {
    if (this.oidcMode) {
      return !this.loading() && !this.loadError() && this.serverTenants().length === 0;
    }
    return this.registry.tenants().length === 0;
  }

  login(): void {
    this.auth.login('/');
  }

  logout(): void {
    this.auth.logout();
  }

  reloadServerTenants(): void {
    this.loading.set(true);
    this.loadError.set('');
    this.api.getTenants().subscribe({
      next: (tenants) => {
        const nameById = new Map(
          this.registry.tenants().map((t) => [t.tenantId.toLowerCase(), t.tenantName])
        );
        this.serverTenants.set(
          tenants.map((t) => ({
            ...t,
            tenantName: nameById.get(t.tenantId.toLowerCase())
          }))
        );
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.loadError.set('Unable to load tenants from the server.');
      }
    });
  }

  openCreateDialog(): void {
    if (this.oidcMode && !this.auth.hasValidAccessToken()) {
      this.auth.login('/');
      return;
    }

    const dialogRef = this.dialog.open(CreateTenantDialogComponent, {
      width: '480px',
      disableClose: false
    });

    dialogRef.afterClosed().subscribe((result) => {
      if (!result) {
        return;
      }

      // Keep a local name cache (and optional masked key for display). Never use
      // the plaintext key as the Dashboard's ongoing Platform.Api session when OIDC is on.
      this.registry.add({
        tenantId: result.tenantId,
        tenantName: result.tenantName,
        apiKeyId: result.apiKeyId,
        apiKeyName: result.apiKeyName,
        maskedKey: TenantRegistryService.mask(result.plaintextKey),
        createdAt: result.createdAt
      });

      if (this.oidcMode) {
        this.reloadServerTenants();
        return;
      }

      // Auth-disabled local mode: activate the returned key as the session credential
      // so Manage can navigate without re-prompting in this tab.
      this.session.signIn(result.tenantId, result.plaintextKey);
    });
  }

  manage(tenant: TenantRegistryEntry | TenantListItem): void {
    const tenantId = tenant.tenantId;

    if (this.oidcMode) {
      if (!this.auth.hasValidAccessToken()) {
        this.auth.login(`/tenants/${tenantId}/services`);
        return;
      }
      this.router.navigate(['/tenants', tenantId, 'services']);
      return;
    }

    // Legacy API-key session path (authentication.enabled = false).
    const entry = tenant as TenantRegistryEntry;
    if (this.session.isActiveTenant(entry.tenantId)) {
      this.router.navigate(['/tenants', entry.tenantId, 'services']);
      return;
    }

    const dialogRef = this.dialog.open(RebindKeyDialogComponent, {
      width: '480px',
      data: { tenantId: entry.tenantId, tenantName: entry.tenantName }
    });

    dialogRef.afterClosed().subscribe((apiKey) => {
      if (!apiKey) {
        return;
      }
      this.router.navigate(['/tenants', entry.tenantId, 'services']);
    });
  }

  displayName(tenant: TenantListItem): string {
    if (tenant.tenantName) {
      return tenant.tenantName;
    }
    // Server does not yet persist display names; show a short id.
    return tenant.tenantId.length > 8 ? `${tenant.tenantId.slice(0, 8)}…` : tenant.tenantId;
  }
}
