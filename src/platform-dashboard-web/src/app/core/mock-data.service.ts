import { Injectable } from '@angular/core';
import { Observable, of } from 'rxjs';
import { delay } from 'rxjs/operators';
import { MetricPoint, MetricSeries, ServiceSummary, TimeRange } from './models';

/**
 * Provides placeholder data for the UI-only build of Platform.Dashboard.
 *
 * This is intentionally NOT wired to Platform.Api yet - this task scope
 * is UI only. Replace with an HTTP client service (GET /v1/services,
 * GET /v1/services/{id}/metrics) in a follow-up task.
 */
@Injectable({ providedIn: 'root' })
export class MockDataService {
  private readonly services: ServiceSummary[] = [
    {
      id: '0dcf943d-de2b-2d98-29af-ba63399dd9c5',
      name: 'checkout-service',
      environment: 'production',
      lastSeenUtc: new Date(Date.now() - 2 * 60_000).toISOString(),
      metricCount: 6
    },
    {
      id: '1a2b3c4d-5e6f-4a1b-8c2d-9e0f1a2b3c4d',
      name: 'inventory-service',
      environment: 'production',
      lastSeenUtc: new Date(Date.now() - 5 * 60_000).toISOString(),
      metricCount: 4
    },
    {
      id: 'aa11bb22-cc33-4d55-8e66-ff7788990011',
      name: 'notifications-worker',
      environment: 'staging',
      lastSeenUtc: new Date(Date.now() - 42 * 60_000).toISOString(),
      metricCount: 3
    }
  ];

  getServices(): Observable<ServiceSummary[]> {
    return of(this.services).pipe(delay(250));
  }

  getServiceById(id: string): Observable<ServiceSummary | undefined> {
    return of(this.services.find(s => s.id === id)).pipe(delay(150));
  }

  getServiceMetrics(serviceId: string, range: TimeRange): Observable<MetricSeries[]> {
    const bucketCount = range === '1h' ? 12 : range === '24h' ? 24 : 14;
    const bucketMinutes = range === '1h' ? 5 : range === '24h' ? 60 : 60 * 24;

    const buildSeries = (metricName: string, unit: string, base: number, spread: number): MetricSeries => {
      const points: MetricPoint[] = Array.from({ length: bucketCount }, (_, i) => {
        const t = Date.now() - (bucketCount - i) * bucketMinutes * 60_000;
        const wobble = Math.sin(i / 2) * spread;
        const avg = Math.max(0, base + wobble + (Math.random() - 0.5) * spread * 0.4);
        return {
          bucketStartUtc: new Date(t).toISOString(),
          min: Math.max(0, avg - spread * 0.6),
          max: avg + spread * 1.4,
          avg,
          p50: avg * 0.95,
          p95: avg * 1.6,
          p99: avg * 2.1,
          count: Math.round(20 + Math.random() * 80)
        };
      });
      return { metricName, unit, points };
    };

    const series: MetricSeries[] = [
      buildSeries('plaxionmediator.request.duration', 'ms', 180, 90),
      buildSeries('plaxionmediator.request.count', 'count', 40, 15),
      buildSeries('plaxionmediator.pipeline_exception.count', 'count', 2, 3)
    ];

    return of(series).pipe(delay(300));
  }
}
