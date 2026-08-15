import { Component } from '@angular/core';
import { Clipboard } from '@angular/cdk/clipboard';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { PlatformApiService } from '../../../core/platform-api.service';
import { CreateTenantResponse } from '../../../core/models';

@Component({
  selector: 'app-create-tenant-dialog',
  standalone: true,
  imports: [
    FormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatTooltipModule
  ],
  templateUrl: './create-tenant-dialog.component.html',
  styleUrl: './create-tenant-dialog.component.scss'
})
export class CreateTenantDialogComponent {
  tenantName = '';
  loading = false;
  error = '';
  result: CreateTenantResponse | null = null;

  constructor(
    private readonly api: PlatformApiService,
    private readonly clipboard: Clipboard,
    private readonly snackBar: MatSnackBar,
    private readonly dialogRef: MatDialogRef<CreateTenantDialogComponent, CreateTenantResponse | undefined>
  ) {}

  submit(): void {
    const trimmed = this.tenantName.trim();
    if (!trimmed) {
      this.error = 'Enter a tenant/organization name.';
      return;
    }

    this.error = '';
    this.loading = true;

    this.api.createTenant(trimmed).subscribe({
      next: (response) => {
        this.loading = false;
        this.result = response;
      },
      error: () => {
        this.loading = false;
        this.error = 'Unable to create tenant. Ensure the backend is running.';
      }
    });
  }

  copyKey(): void {
    if (!this.result) {
      return;
    }
    this.clipboard.copy(this.result.plaintextKey);
    this.snackBar.open('Copied!', undefined, { duration: 2000 });
  }

  done(): void {
    this.dialogRef.close(this.result ?? undefined);
  }
}
