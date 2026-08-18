import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';

export interface AppConfig {
  apiBaseUrl?: string;
}

@Injectable({ providedIn: 'root' })
export class AppConfigService {
  private config: AppConfig = {};
  private configLoaded = false;

  constructor(private readonly http: HttpClient) {}

  async loadConfig(): Promise<void> {
    try {
      const data = await firstValueFrom(this.http.get<AppConfig>('assets/config.json'));
      this.config = data || {};
      this.configLoaded = true;
    } catch (err) {
      // Fallback to compiled environment if file is missing or invalid
      this.config = {};
      this.configLoaded = false;
    }
  }

  get apiBaseUrl(): string {
    // An explicitly present (even empty-string) apiBaseUrl in config.json is a deliberate
    // "same-origin" instruction from the container entrypoint and must NOT fall back to the
    // compiled environment default (which may point at an unreachable placeholder host).
    // Only fall back when config.json couldn't be loaded at all or omits the key entirely.
    if (this.configLoaded && this.config.apiBaseUrl !== undefined) {
      return this.config.apiBaseUrl;
    }
    return environment.apiBaseUrl;
  }
}
