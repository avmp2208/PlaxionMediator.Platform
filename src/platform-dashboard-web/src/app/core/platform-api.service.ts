import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import {
  ApiKeyListItem,
  CreateApiKeyResponse,
  CreateTenantResponse,
  MetricPoint,
  MetricSeries,
  ServiceSummary,
  TimeRange
} from './models';
import { environment } from '../../environments/environment';

@Injectable({ providedIn: 'root' })
export class PlatformApiService {
  private readonly baseUrl = environment.apiBaseUrl;

  constructor(private readonly http: HttpClient) {}

  getServices(tenantId: string): Observable<ServiceSummary[]> {
    return this.http.get<ServiceSummary[]>(`${this.baseUrl}/v1/tenants/${tenantId}/services`);
  }

  createTenant(name: string): Observable<CreateTenantResponse> {
    return this.http.post<CreateTenantResponse>(`${this.baseUrl}/v1/tenants`, { name });
  }

  listApiKeys(): Observable<ApiKeyListItem[]> {
    return this.http.get<ApiKeyListItem[]>(`${this.baseUrl}/v1/api-keys`);
  }

  createApiKey(name: string): Observable<CreateApiKeyResponse> {
    return this.http.post<CreateApiKeyResponse>(`${this.baseUrl}/v1/api-keys`, { name });
  }

  revokeApiKey(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/v1/api-keys/${id}`);
  }

  whoAmI(): Observable<{ tenantId: string }> {
    return this.http.get<{ tenantId: string }>(`${this.baseUrl}/v1/api-keys/whoami`);
  }

  getServiceById(tenantId: string, id: string): Observable<ServiceSummary | undefined> {
    return this.getServices(tenantId).pipe(
      map(services => services.find(s => s.id === id))
    );
  }

  getServiceMetrics(tenantId: string, serviceId: string, range: TimeRange): Observable<MetricSeries[]> {
    return this.http.get<MetricPoint[]>(`${this.baseUrl}/v1/tenants/${tenantId}/services/${serviceId}/metrics`, {
      params: { range }
    }).pipe(
      map(points => this.groupMetrics(points))
    );
  }

  private groupMetrics(points: MetricPoint[]): MetricSeries[] {
    const grouped = new Map<string, MetricPoint[]>();

    for (const p of points) {
      const existing = grouped.get(p.metricName) || [];
      existing.push(p);
      grouped.set(p.metricName, existing);
    }

    return Array.from(grouped.entries()).map(([metricName, points]) => ({
      metricName,
      unit: this.deriveUnit(metricName),
      points: points.sort((a, b) => a.bucketStart.localeCompare(b.bucketStart))
    }));
  }

  private deriveUnit(metricName: string): string {
    if (metricName.endsWith('.duration')) return 'ms';
    if (metricName.endsWith('.count')) return 'count';
    if (metricName.endsWith('.size')) return 'bytes';
    return '';
  }
}
