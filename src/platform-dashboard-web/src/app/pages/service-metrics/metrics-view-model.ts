import { MetricPoint } from '../../core/models';

const REQUEST_COUNT_METRIC = 'plaxionmediator.request.count';
const REQUEST_DURATION_METRIC = 'plaxionmediator.request.duration';
const PIPELINE_EXCEPTION_METRIC = 'plaxionmediator.pipeline_exception.count';

export interface MetricsOverview {
  totalExecutions: number;
  p50: number;
  p95: number;
  p99: number;
  failurePercent: number;
  distinctRequestTypeCount: number;
}

export interface ThroughputPoint {
  time: Date;
  count: number;
}

export interface LatencyPoint {
  time: Date;
  p50: number;
  p95: number;
  p99: number;
}

export interface FailureRatePoint {
  time: Date;
  failureRatePercent: number;
}

export interface MetricsViewModel {
  overview: MetricsOverview;
  throughput: ThroughputPoint[];
  latency: LatencyPoint[];
  failureRate: FailureRatePoint[];
  /** True when there is at least one non-zero failure rate value or a pipeline_exception.count row in the raw data. */
  hasFailureData: boolean;
}

/**
 * Pure, framework-free transformation of the raw metric rollup rows returned by
 * `GET /v1/tenants/{tenantId}/services/{id}/metrics` into the shapes the service-metrics
 * dashboard renders (summary cards, throughput/latency/failure-rate charts, ranking table).
 *
 * All aggregation choices made here are deliberate and documented inline, since the raw API
 * response can contain multiple rows per bucket (one per distinct requestType) and this module
 * is the single place responsible for collapsing that into per-bucket/overall figures.
 */
export function buildMetricsViewModel(points: MetricPoint[]): MetricsViewModel {
  const requestCountRows = points.filter(p => p.metricName === REQUEST_COUNT_METRIC && p.requestType != null);
  const durationRows = points.filter(p => p.metricName === REQUEST_DURATION_METRIC && p.requestType != null);
  const exceptionRows = points.filter(p => p.metricName === PIPELINE_EXCEPTION_METRIC && p.requestType != null);

  const overview = buildOverview(requestCountRows, durationRows, exceptionRows);
  const throughput = buildThroughput(requestCountRows);
  const latency = buildLatency(durationRows);
  const failureRate = buildFailureRate(requestCountRows, exceptionRows);

  const hasFailureData = exceptionRows.length > 0 || failureRate.some(f => f.failureRatePercent > 0);

  return { overview, throughput, latency, failureRate, hasFailureData };
}

function bucketKey(bucketStart: string): string {
  return bucketStart;
}

function exceptionKey(requestType: string, bucketStart: string): string {
  return `${requestType}::${bucketStart}`;
}

function buildOverview(
  requestCountRows: MetricPoint[],
  durationRows: MetricPoint[],
  exceptionRows: MetricPoint[]
): MetricsOverview {
  const totalExecutions = requestCountRows.reduce((sum, p) => sum + p.count, 0);
  const totalExceptions = exceptionRows.reduce((sum, p) => sum + p.count, 0);
  const failurePercent = totalExecutions === 0 ? 0 : (totalExceptions / totalExecutions) * 100;

  // Overall P50/P95/P99: a count-weighted average across all (requestType, bucket) duration rows.
  // This is the simplest defensible approach that avoids the "max of maxes" distortion while still
  // giving more weight to buckets/request types that actually saw more traffic.
  const { p50, p95, p99 } = weightedPercentiles(durationRows);

  const distinctRequestTypeCount = new Set(requestCountRows.map(p => p.requestType as string)).size;

  return { totalExecutions, p50, p95, p99, failurePercent, distinctRequestTypeCount };
}

function weightedPercentiles(durationRows: MetricPoint[]): { p50: number; p95: number; p99: number } {
  const totalCount = durationRows.reduce((sum, p) => sum + p.count, 0);
  if (totalCount === 0) {
    return { p50: 0, p95: 0, p99: 0 };
  }

  let p50 = 0;
  let p95 = 0;
  let p99 = 0;
  for (const row of durationRows) {
    const weight = row.count / totalCount;
    p50 += row.p50 * weight;
    p95 += row.p95 * weight;
    p99 += row.p99 * weight;
  }

  return { p50, p95, p99 };
}

function buildThroughput(requestCountRows: MetricPoint[]): ThroughputPoint[] {
  const byBucket = new Map<string, number>();
  for (const row of requestCountRows) {
    const key = bucketKey(row.bucketStart);
    byBucket.set(key, (byBucket.get(key) ?? 0) + row.count);
  }

  return Array.from(byBucket.entries())
    .map(([bucketStart, count]) => ({ time: new Date(bucketStart), count }))
    .sort((a, b) => a.time.getTime() - b.time.getTime());
}

function buildLatency(durationRows: MetricPoint[]): LatencyPoint[] {
  // Multiple requestTypes can share a bucket; aggregate them with a count-weighted average of
  // p50/p95/p99, consistent with the overall-overview aggregation approach above.
  const byBucket = new Map<string, MetricPoint[]>();
  for (const row of durationRows) {
    const key = bucketKey(row.bucketStart);
    const existing = byBucket.get(key) ?? [];
    existing.push(row);
    byBucket.set(key, existing);
  }

  return Array.from(byBucket.entries())
    .map(([bucketStart, rows]) => {
      const { p50, p95, p99 } = weightedPercentiles(rows);
      return { time: new Date(bucketStart), p50, p95, p99 };
    })
    .sort((a, b) => a.time.getTime() - b.time.getTime());
}

function buildFailureRate(requestCountRows: MetricPoint[], exceptionRows: MetricPoint[]): FailureRatePoint[] {
  const exceptionByKey = new Map<string, number>();
  for (const row of exceptionRows) {
    exceptionByKey.set(exceptionKey(row.requestType as string, row.bucketStart), row.count);
  }

  const requestsByBucket = new Map<string, number>();
  const exceptionsByBucket = new Map<string, number>();

  for (const row of requestCountRows) {
    const bKey = bucketKey(row.bucketStart);
    requestsByBucket.set(bKey, (requestsByBucket.get(bKey) ?? 0) + row.count);

    // Absence of a matching pipeline_exception.count row means zero exceptions for that
    // requestType+bucket, not "unknown" - so we default to 0 rather than skipping.
    const exceptionsForRow = exceptionByKey.get(exceptionKey(row.requestType as string, row.bucketStart)) ?? 0;
    exceptionsByBucket.set(bKey, (exceptionsByBucket.get(bKey) ?? 0) + exceptionsForRow);
  }

  return Array.from(requestsByBucket.entries())
    .map(([bucketStart, totalRequests]) => {
      const totalExceptions = exceptionsByBucket.get(bucketStart) ?? 0;
      const failureRatePercent = totalRequests === 0 ? 0 : (totalExceptions / totalRequests) * 100;
      return { time: new Date(bucketStart), failureRatePercent };
    })
    .sort((a, b) => a.time.getTime() - b.time.getTime());
}
