import { Injectable, signal } from '@angular/core';

const STORAGE_KEY = 'platform.apiKey';

/**
 * Holds the tenant's API key for the current browser session.
 */
@Injectable({ providedIn: 'root' })
export class TenantSessionService {
  private readonly apiKeySignal = signal<string | null>(sessionStorage.getItem(STORAGE_KEY));

  readonly apiKey = this.apiKeySignal.asReadonly();

  get isAuthenticated(): boolean {
    return !!this.apiKeySignal();
  }

  signIn(apiKey: string): void {
    sessionStorage.setItem(STORAGE_KEY, apiKey);
    this.apiKeySignal.set(apiKey);
  }

  signOut(): void {
    sessionStorage.removeItem(STORAGE_KEY);
    this.apiKeySignal.set(null);
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
