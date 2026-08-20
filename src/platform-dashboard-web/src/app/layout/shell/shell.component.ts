import { Component } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TenantSessionService } from '../../core/tenant-session.service';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    MatIconModule,
    MatListModule,
    MatSidenavModule,
    MatToolbarModule,
    MatButtonModule,
    MatTooltipModule
  ],
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.scss'
})
export class ShellComponent {
  constructor(
    private readonly session: TenantSessionService,
    readonly auth: AuthService,
    private readonly router: Router
  ) {}

  goToTenants(): void {
    // In legacy API-key mode, leaving a tenant clears the session key.
    // In OIDC mode the access token stays; only explicit Sign out clears it.
    if (!this.auth.authenticationEnabled) {
      this.session.signOut();
    }
    this.router.navigateByUrl('/');
  }

  logout(): void {
    if (this.auth.authenticationEnabled) {
      this.auth.logout();
      return;
    }
    this.session.signOut();
    this.router.navigateByUrl('/');
  }
}
