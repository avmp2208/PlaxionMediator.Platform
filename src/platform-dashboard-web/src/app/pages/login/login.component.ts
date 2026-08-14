import { Component } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TenantSessionService } from '../../core/tenant-session.service';
import { PlatformApiService } from '../../core/platform-api.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [
    FormsModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule
  ],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss'
})
export class LoginComponent {
  apiKey = '';
  hideKey = true;
  loading = false;
  error = '';

  constructor(
    private readonly session: TenantSessionService,
    private readonly api: PlatformApiService,
    private readonly router: Router
  ) {}

  submit(): void {
    const trimmed = this.apiKey.trim();
    if (!trimmed) {
      this.error = 'Enter your tenant API key to continue.';
      return;
    }

    this.error = '';
    this.loading = true;

    // Temporarily set the key to allow the interceptor to use it for validation
    this.session.signIn(trimmed);

    this.api.getServices().subscribe({
      next: () => {
        this.loading = false;
        this.router.navigateByUrl('/services');
      },
      error: (err) => {
        this.loading = false;
        this.session.signOut();
        if (err.status === 401) {
          this.error = 'Invalid API key. Please check your credentials.';
        } else {
          this.error = 'Unable to connect to Platform.Api. Ensure the backend is running.';
        }
      }
    });
  }
}
