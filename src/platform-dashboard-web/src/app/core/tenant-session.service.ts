import { Injectable, signal } from '@angular/core';

const STORAGE_KEY = 'platform.apiKey';
const TENANT_ID_STORAGE_KEY = 'platform.apiKey.tenantId';

/**
 * Holds the tenant's API key for the current browser session. Every
 * services/metrics request is gated server-side by [Authorize] +
 * TenantId-from-key validation (see ServicesController/
 * ServiceMetricsController in Platform.Api), so the active key here must
 * belong to the tenant being viewed - it is tracked alongside the key so
 * "Manage" can tell whether a re-prompt for the key is needed.
 */
@Injectable({ providedIn: 'root' })
export class TenantSessionService {
  private readonly apiKeySignal = signal<string | null>(sessionStorage.getItem(STORAGE_KEY));
  private readonly tenantIdSignal = signal<string | null>(sessionStorage.getItem(TENANT_ID_STORAGE_KEY));

  readonly apiKey = this.apiKeySignal.asReadonly();
  readonly tenantId = this.tenantIdSignal.asReadonly();

  get isAuthenticated(): boolean {
    return !!this.apiKeySignal();
  }

  /** Whether the active session key belongs to the given tenant. */
  isActiveTenant(tenantId: string): boolean {
    return !!this.apiKeySignal() && this.tenantIdSignal() === tenantId;
  }

  signIn(tenantId: string, apiKey: string): void {
    sessionStorage.setItem(STORAGE_KEY, apiKey);
    sessionStorage.setItem(TENANT_ID_STORAGE_KEY, tenantId);
    this.apiKeySignal.set(apiKey);
    this.tenantIdSignal.set(tenantId);
  }

  signOut(): void {
    sessionStorage.removeItem(STORAGE_KEY);
    sessionStorage.removeItem(TENANT_ID_STORAGE_KEY);
    this.apiKeySignal.set(null);
    this.tenantIdSignal.set(null);
  }

  /** Masked representation safe to render in the UI (never log/display the raw key). */
  maskedApiKey(): string {
    const key = this.apiKeySignal();
    if (!key) {
      return '';
    }
    const visible = key.slice(-4);
    return `••••••••••••${visible}`;
  }
}
