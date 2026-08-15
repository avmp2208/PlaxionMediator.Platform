import { Injectable, signal } from '@angular/core';
import { TenantRegistryEntry } from './models';

const STORAGE_KEY = 'platform.tenantRegistry';

/**
 * Local (browser-scoped) registry of tenants the user has created on this device.
 * There is no cross-tenant admin login in this MVP, so this is the only record of
 * "which tenants did I create" the dashboard has. Only masked API keys are persisted here;
 * the plaintext key is never stored beyond the moment it is shown to the user.
 */
@Injectable({ providedIn: 'root' })
export class TenantRegistryService {
  private readonly tenantsSignal = signal<TenantRegistryEntry[]>(this.readFromStorage());

  readonly tenants = this.tenantsSignal.asReadonly();

  add(entry: TenantRegistryEntry): void {
    const next = [...this.tenantsSignal(), entry];
    this.tenantsSignal.set(next);
    this.writeToStorage(next);
  }

  /** Masks a raw API key for display, matching TenantSessionService.maskedApiKey(). */
  static mask(apiKey: string): string {
    const visible = apiKey.slice(-4);
    return `••••••••••••${visible}`;
  }

  private readFromStorage(): TenantRegistryEntry[] {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      return raw ? (JSON.parse(raw) as TenantRegistryEntry[]) : [];
    } catch {
      return [];
    }
  }

  private writeToStorage(entries: TenantRegistryEntry[]): void {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(entries));
  }
}
