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
import { TenantRegistryEntry } from '../../core/models';
import { CreateTenantDialogComponent } from './create-tenant-dialog/create-tenant-dialog.component';

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
    });
  }

  manage(tenant: TenantRegistryEntry): void {
    // Viewing a tenant's services only requires its TenantId (see
    // ServicesController in Platform.Api) - no API key/authentication is
    // needed at this MVP stage, so "Manage" navigates straight through.
    this.router.navigate(['/tenants', tenant.tenantId, 'services']);
  }
}
