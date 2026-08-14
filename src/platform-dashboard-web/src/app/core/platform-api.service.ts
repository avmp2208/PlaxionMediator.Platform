import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { MetricPoint, MetricSeries, ServiceSummary, TimeRange } from './models';
import { environment } from '../../environments/environment';

@Injectable({ providedIn: 'root' })
export class PlatformApiService {
  private readonly baseUrl = environment.apiBaseUrl;

  constructor(private readonly http: HttpClient) {}

  getServices(): Observable<ServiceSummary[]> {
    return this.http.get<ServiceSummary[]>(`${this.baseUrl}/v1/services`);
  }

  getServiceById(id: string): Observable<ServiceSummary | undefined> {
    return this.getServices().pipe(
      map(services => services.find(s => s.id === id))
    );
  }

  getServiceMetrics(serviceId: string, range: TimeRange): Observable<MetricSeries[]> {
    return this.http.get<MetricPoint[]>(`${this.baseUrl}/v1/services/${serviceId}/metrics`, {
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
