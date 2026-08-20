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
  PagedRequestTypeRankingResponse,
  ServiceSummary,
  TenantListItem,
  TimeRange
} from './models';
import { AppConfigService } from './app-config.service';

@Injectable({ providedIn: 'root' })
export class PlatformApiService {
  private get baseUrl() { return this.config.apiBaseUrl; }

  constructor(
    private readonly http: HttpClient,
    private readonly config: AppConfigService
  ) {}

  getServices(tenantId: string): Observable<ServiceSummary[]> {
    return this.http.get<ServiceSummary[]>(`${this.baseUrl}/v1/tenants/${tenantId}/services`);
  }

  /** JWT-authenticated list of tenants the current user belongs to. */
  getTenants(): Observable<TenantListItem[]> {
    return this.http.get<TenantListItem[]>(`${this.baseUrl}/v1/tenants`);
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
    return this.getServiceMetricsRaw(tenantId, serviceId, range).pipe(
      map(points => this.groupMetrics(points))
    );
  }

  /** Returns the raw rollup rows as returned by the API, without any grouping/aggregation applied. */
  getServiceMetricsRaw(tenantId: string, serviceId: string, range: TimeRange): Observable<MetricPoint[]> {
    return this.http.get<MetricPoint[]>(`${this.baseUrl}/v1/tenants/${tenantId}/services/${serviceId}/metrics`, {
      params: { range }
    });
  }

  /**
   * Server-side-paginated request-type ranking, ordered by the server (executionCount descending,
   * requestType ascending tie-break). The frontend must not re-sort this data client-side.
   */
  getRequestTypeRanking(
    tenantId: string,
    serviceId: string,
    range: TimeRange,
    page: number,
    pageSize: number
  ): Observable<PagedRequestTypeRankingResponse> {
    return this.http.get<PagedRequestTypeRankingResponse>(
      `${this.baseUrl}/v1/tenants/${tenantId}/services/${serviceId}/request-types`,
      { params: { range, page, pageSize } }
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
