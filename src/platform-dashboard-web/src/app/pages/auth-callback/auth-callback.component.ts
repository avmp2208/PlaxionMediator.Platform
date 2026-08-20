import { Component, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AuthService } from '../../core/auth.service';

/**
 * OIDC redirect callback landing route. angular-oauth2-oidc completes the
 * code+PKCE exchange during AuthService.init(); this component only routes
 * the user onward (or shows a minimal error if login did not succeed).
 */
@Component({
  selector: 'app-auth-callback',
  standalone: true,
  imports: [MatProgressSpinnerModule],
  template: `
    <div class="callback-wrap">
      @if (error) {
        <p class="error-text">{{ error }}</p>
        <button type="button" (click)="retry()">Try again</button>
      } @else {
        <mat-spinner diameter="40"></mat-spinner>
        <p>Completing sign-in…</p>
      }
    </div>
  `,
  styles: [
    `
      .callback-wrap {
        min-height: 40vh;
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        gap: 16px;
        color: var(--app-text-secondary);
      }
      .error-text {
        color: var(--app-error);
      }
      button {
        background: var(--app-accent);
        color: #0f1115;
        border: 0;
        border-radius: 4px;
        padding: 8px 16px;
        cursor: pointer;
      }
    `
  ]
})
export class AuthCallbackComponent implements OnInit {
  error = '';

  constructor(
    private readonly auth: AuthService,
    private readonly router: Router
  ) {}

  async ngOnInit(): Promise<void> {
    // init() is idempotent; ensures discovery + tryLogin finished.
    await this.auth.init();

    if (!this.auth.authenticationEnabled) {
      await this.router.navigateByUrl('/');
      return;
    }

    if (this.auth.hasValidAccessToken()) {
      const returnUrl = this.auth.getReturnUrl() || '/';
      await this.router.navigateByUrl(returnUrl);
      return;
    }

    this.error = 'Sign-in did not complete. Please try again.';
  }

  retry(): void {
    this.auth.login('/');
  }
}
