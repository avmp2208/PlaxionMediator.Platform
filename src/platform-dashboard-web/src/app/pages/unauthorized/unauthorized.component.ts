import { Component } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-unauthorized',
  standalone: true,
  imports: [MatCardModule, MatButtonModule, MatIconModule, RouterLink],
  template: `
    <div class="page">
      <mat-card class="card">
        <mat-icon class="icon">lock</mat-icon>
        <h1>{{ title }}</h1>
        <p>{{ message }}</p>
        <div class="actions">
          <a mat-stroked-button routerLink="/">Back to tenants</a>
          @if (auth.authenticationEnabled) {
            <button mat-flat-button color="primary" type="button" (click)="login()">Sign in</button>
          }
        </div>
      </mat-card>
    </div>
  `,
  styles: [
    `
      .page {
        min-height: 60vh;
        display: flex;
        align-items: center;
        justify-content: center;
        padding: 24px;
      }
      .card {
        max-width: 480px;
        width: 100%;
        text-align: center;
        padding: 32px 24px;
        background: var(--app-surface);
        border: 1px solid var(--app-border);
      }
      .icon {
        font-size: 40px;
        width: 40px;
        height: 40px;
        color: var(--app-warning);
        margin-bottom: 8px;
      }
      h1 {
        margin: 0 0 8px;
        font-size: 1.35rem;
      }
      p {
        color: var(--app-text-secondary);
        margin: 0 0 20px;
      }
      .actions {
        display: flex;
        gap: 12px;
        justify-content: center;
        flex-wrap: wrap;
      }
    `
  ]
})
export class UnauthorizedComponent {
  readonly status: number;
  readonly title: string;
  readonly message: string;

  constructor(
    readonly auth: AuthService,
    route: ActivatedRoute
  ) {
    const raw = route.snapshot.queryParamMap.get('status');
    this.status = raw ? Number(raw) : 401;
    if (this.status === 403) {
      this.title = 'Forbidden';
      this.message = 'You are signed in but do not have access to this resource.';
    } else {
      this.title = 'Unauthorized';
      this.message = 'Your session is missing or expired. Sign in again to continue.';
    }
  }

  login(): void {
    this.auth.login('/');
  }
}
