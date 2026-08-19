import { Component, Inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { PlatformApiService } from '../../../core/platform-api.service';
import { TenantSessionService } from '../../../core/tenant-session.service';

export interface RebindKeyDialogData {
  tenantId: string;
  tenantName: string;
}

/**
 * Minimal "paste your API key" re-auth prompt. Shown by TenantsComponent's
 * "Manage" flow only when the tenant being managed does not match the
 * currently active in-memory session key (e.g. the tenant was created in a
 * previous browser session/tab, so its plaintext key is no longer held in
 * memory). Never prompts for the tenant that was just created in this tab.
 */
@Component({
  selector: 'app-rebind-key-dialog',
  standalone: true,
  imports: [
    FormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule
  ],
  templateUrl: './rebind-key-dialog.component.html',
  styleUrl: './rebind-key-dialog.component.scss'
})
export class RebindKeyDialogComponent {
  apiKey = '';
  loading = false;
  error = '';

  constructor(
    private readonly api: PlatformApiService,
    private readonly session: TenantSessionService,
    private readonly dialogRef: MatDialogRef<RebindKeyDialogComponent, string | undefined>,
    @Inject(MAT_DIALOG_DATA) readonly data: RebindKeyDialogData
  ) {}

  submit(): void {
    const trimmed = this.apiKey.trim();
    if (!trimmed) {
      this.error = 'Enter the API key for this tenant.';
      return;
    }

    this.error = '';
    this.loading = true;

    // Tentatively activate the entered key so the interceptor attaches it,
    // then validate it actually authenticates for this tenant.
    this.session.signIn(this.data.tenantId, trimmed);

    this.api.getServices(this.data.tenantId).subscribe({
      next: () => {
        this.loading = false;
        this.dialogRef.close(trimmed);
      },
      error: () => {
        this.loading = false;
        this.session.signOut();
        this.error = 'That key was not accepted for this tenant.';
      }
    });
  }

  cancel(): void {
    this.dialogRef.close(undefined);
  }
}
