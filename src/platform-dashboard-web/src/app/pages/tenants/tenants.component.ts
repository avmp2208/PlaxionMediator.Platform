import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TenantRegistryService } from '../../core/tenant-registry.service';
import { TenantSessionService } from '../../core/tenant-session.service';
import { TenantRegistryEntry } from '../../core/models';
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
    MatTooltipModule
  ],
  templateUrl: './tenants.component.html',
  styleUrl: './tenants.component.scss'
})
export class TenantsComponent {
  readonly displayedColumns = ['name', 'maskedKey', 'createdAt', 'actions'];

  constructor(
    readonly registry: TenantRegistryService,
    private readonly session: TenantSessionService,
    private readonly dialog: MatDialog,
    private readonly router: Router
  ) {}

  openCreateDialog(): void {
    const dialogRef = this.dialog.open(CreateTenantDialogComponent, {
      width: '480px',
      disableClose: false
    });

    dialogRef.afterClosed().subscribe((result) => {
      if (!result) {
        return;
      }

      this.registry.add({
        tenantId: result.tenantId,
        tenantName: result.tenantName,
        apiKeyId: result.apiKeyId,
        apiKeyName: result.apiKeyName,
        maskedKey: TenantRegistryService.mask(result.plaintextKey),
        createdAt: result.createdAt
      });

      // The plaintext key is already in-memory from the create response for
      // this browser tab - activate it as the session key immediately so
      // "Manage" can navigate straight through without re-prompting.
      this.session.signIn(result.tenantId, result.plaintextKey);
    });
  }

  manage(tenant: TenantRegistryEntry): void {
    // Services/metrics are gated server-side by [Authorize] +
    // TenantId-from-key validation (see ServicesController/
    // ServiceMetricsController in Platform.Api). If the active session key
    // already belongs to this tenant (e.g. it was just created in this
    // tab), navigate straight through. Otherwise (e.g. the tenant was
    // created in a previous session/tab and its plaintext key is no longer
    // in memory), prompt for the key before navigating.
    if (this.session.isActiveTenant(tenant.tenantId)) {
      this.router.navigate(['/tenants', tenant.tenantId, 'services']);
      return;
    }

    const dialogRef = this.dialog.open(RebindKeyDialogComponent, {
      width: '480px',
      data: { tenantId: tenant.tenantId, tenantName: tenant.tenantName }
    });

    dialogRef.afterClosed().subscribe((apiKey) => {
      if (!apiKey) {
        return;
      }

      this.router.navigate(['/tenants', tenant.tenantId, 'services']);
    });
  }
}
