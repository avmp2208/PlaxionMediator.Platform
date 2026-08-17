import { MetricPoint } from '../../core/models';

const REQUEST_COUNT_METRIC = 'plaxionmediator.request.count';
const REQUEST_DURATION_METRIC = 'plaxionmediator.request.duration';
const PIPELINE_EXCEPTION_METRIC = 'plaxionmediator.pipeline_exception.count';

export interface MetricsOverview {
  totalExecutions: number;
  /** null when there are no executions in range - there is no latency to report, so it must never
   * fall back to 0ms (which would incorrectly imply an actual zero-latency execution). */
  p50: number | null;
  p95: number | null;
  p99: number | null;
  failurePercent: number;
  distinctRequestTypeCount: number;
}

export interface ThroughputPoint {
  time: Date;
  count: number;
}

export interface LatencyPoint {
  time: Date;
  /** null means "no duration observations in this bucket" - a gap, never a carried-forward value. */
  p50: number | null;
  p95: number | null;
  p99: number | null;
}

export interface FailureRatePoint {
  time: Date;
  /**
   * Never carried forward from a previous bucket. A bucket with zero executions (idle) reports
   * 0% here - failure rate is only meaningful relative to executions that happened, so "no
   * executions" is unambiguously "no failures" for dashboard visualization purposes. This differs
   * from latency, where a lack of observations must stay null/gap rather than 0.
   */
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

/** Fallback bucket cadence (matches the backend's fixed 1-minute rollup bucket) used to build the
 * full range grid when no data rows are present to infer the cadence from. */
const DEFAULT_BUCKET_STEP_MS = 60_000;

export interface MetricsViewModelRange {
  /** Start of the selected time range (e.g. "now - 1h"). */
  rangeStart: Date;
  /** End of the selected time range - normally "now", computed once by the caller. */
  rangeEnd: Date;
}

/**
 * Pure, framework-free transformation of the raw metric rollup rows returned by
 * `GET /v1/tenants/{tenantId}/services/{id}/metrics` into the shapes the service-metrics
 * dashboard renders (summary cards, throughput/latency/failure-rate charts, ranking table).
 *
 * All aggregation choices made here are deliberate and documented inline, since the raw API
 * response can contain multiple rows per bucket (one per distinct requestType) and this module
 * is the single place responsible for collapsing that into per-bucket/overall figures.
 *
 * `range`, when supplied, forces the generated bucket grid to always span the *entire* selected
 * window - from `rangeStart` through `rangeEnd` (normally "now") - regardless of where the actual
 * data happens to stop. Without this, a chart would visually stop at the last received metric
 * instead of continuing (with explicit zero/null buckets) all the way to the current time, which
 * is indistinguishable from the app being stuck/frozen. When omitted (e.g. in unit tests that only
 * care about the data-driven shape), the grid falls back to spanning just the observed data.
 */
export function buildMetricsViewModel(points: MetricPoint[], range?: MetricsViewModelRange): MetricsViewModel {
  const requestCountRows = points.filter(p => p.metricName === REQUEST_COUNT_METRIC && p.requestType != null);
  const durationRows = points.filter(p => p.metricName === REQUEST_DURATION_METRIC && p.requestType != null);
  const exceptionRows = points.filter(p => p.metricName === PIPELINE_EXCEPTION_METRIC && p.requestType != null);

  const overview = buildOverview(requestCountRows, durationRows, exceptionRows);

  // A complete grid of every bucket boundary between the earliest and latest bucket seen in the
  // response (inclusive), at the rollup's own bucket size. This is what makes "no data in this
  // bucket" an explicit, independently-computed gap instead of the point simply being missing from
  // the array - which would let the chart library interpolate/draw a line straight from the last
  // real value to the next one, visually looking like the old value "carried forward".
  const bucketStarts = allBucketStarts(
    [requestCountRows, durationRows, exceptionRows],
    range ? { start: range.rangeStart.getTime(), end: range.rangeEnd.getTime() } : undefined
  );

  const throughput = buildThroughput(requestCountRows, bucketStarts);
  const latency = buildLatency(durationRows, bucketStarts);
  const failureRate = buildFailureRate(requestCountRows, exceptionRows, bucketStarts);

  const hasFailureData = exceptionRows.length > 0 || failureRate.some(f => f.failureRatePercent > 0);

  return { overview, throughput, latency, failureRate, hasFailureData };
}

/**
 * Every distinct bucket boundary (as epoch millis) observed across all three metric row sets, plus
 * every boundary in between at the same cadence, so downstream series can report an explicit value
 * (or null gap) for each bucket independently rather than only for buckets that happened to have data.
 */
function allBucketStarts(rowSets: MetricPoint[][], range?: { start: number; end: number }): number[] {
  const observed = new Set<number>();
  let step: number | null = null;

  for (const rows of rowSets) {
    for (const row of rows) {
      observed.add(new Date(row.bucketStart).getTime());
      const rowStep = new Date(row.bucketEnd).getTime() - new Date(row.bucketStart).getTime();
      if (rowStep > 0 && (step === null || rowStep < step)) {
        step = rowStep;
      }
    }
  }

  if (range) {
    // Fixed-window mode: the grid always spans the *entire* selected range end-to-end (aligned to
    // the bucket cadence), regardless of where the actual observed data stops - this is what makes
    // the chart keep extending all the way to "now" instead of stalling at the last real metric.
    const effectiveStep = step ?? DEFAULT_BUCKET_STEP_MS;
    const start = alignToBucket(range.start, effectiveStep);
    const end = alignToBucket(range.end, effectiveStep);

    const grid: number[] = [];
    for (let t = start; t <= end; t += effectiveStep) {
      grid.push(t);
    }

    return grid;
  }

  // No fixed range supplied - fall back to spanning only the observed data (used by tests that
  // don't care about "extend to now" semantics).
  if (observed.size === 0) {
    return [];
  }

  if (step === null) {
    return Array.from(observed).sort((a, b) => a - b);
  }

  const min = Math.min(...observed);
  const max = Math.max(...observed);

  const grid: number[] = [];
  for (let t = min; t <= max; t += step) {
    grid.push(t);
  }

  return grid;
}

function alignToBucket(ms: number, step: number): number {
  return ms - (ms % step);
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
  // giving more weight to buckets/request types that actually saw more traffic. No executions at
  // all means there is nothing to report - null, not 0ms.
  const totalDurationCount = durationRows.reduce((sum, p) => sum + p.count, 0);
  const { p50, p95, p99 } = totalDurationCount === 0 ? { p50: null, p95: null, p99: null } : weightedPercentiles(durationRows);

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

function buildThroughput(requestCountRows: MetricPoint[], bucketStarts: number[]): ThroughputPoint[] {
  const byBucket = new Map<number, number>();
  for (const row of requestCountRows) {
    const key = new Date(row.bucketStart).getTime();
    byBucket.set(key, (byBucket.get(key) ?? 0) + row.count);
  }

  // Throughput is the one series where "no executions in this bucket" is a real, valid data point
  // (0 requests), not a gap - so every grid bucket without a matching row explicitly reports 0.
  return bucketStarts
    .map(t => ({ time: new Date(t), count: byBucket.get(t) ?? 0 }))
    .sort((a, b) => a.time.getTime() - b.time.getTime());
}

function buildLatency(durationRows: MetricPoint[], bucketStarts: number[]): LatencyPoint[] {
  // Multiple requestTypes can share a bucket; aggregate them with a count-weighted average of
  // p50/p95/p99, consistent with the overall-overview aggregation approach above.
  const byBucket = new Map<number, MetricPoint[]>();
  for (const row of durationRows) {
    const key = new Date(row.bucketStart).getTime();
    const existing = byBucket.get(key) ?? [];
    existing.push(row);
    byBucket.set(key, existing);
  }

  // A bucket with zero duration observations has no latency to report - null/gap, never the
  // previous bucket's value.
  return bucketStarts
    .map(t => {
      const rows = byBucket.get(t);
      if (!rows || rows.length === 0) {
        return { time: new Date(t), p50: null, p95: null, p99: null };
      }

      const { p50, p95, p99 } = weightedPercentiles(rows);
      return { time: new Date(t), p50, p95, p99 };
    })
    .sort((a, b) => a.time.getTime() - b.time.getTime());
}

function buildFailureRate(
  requestCountRows: MetricPoint[],
  exceptionRows: MetricPoint[],
  bucketStarts: number[]
): FailureRatePoint[] {
  const exceptionByKey = new Map<string, number>();
  for (const row of exceptionRows) {
    exceptionByKey.set(exceptionKey(row.requestType as string, row.bucketStart), row.count);
  }

  const requestsByBucket = new Map<number, number>();
  const exceptionsByBucket = new Map<number, number>();

  for (const row of requestCountRows) {
    const bKey = new Date(row.bucketStart).getTime();
    requestsByBucket.set(bKey, (requestsByBucket.get(bKey) ?? 0) + row.count);

    // Absence of a matching pipeline_exception.count row means zero exceptions for that
    // requestType+bucket, not "unknown" - so we default to 0 rather than skipping.
    const exceptionsForRow = exceptionByKey.get(exceptionKey(row.requestType as string, row.bucketStart)) ?? 0;
    exceptionsByBucket.set(bKey, (exceptionsByBucket.get(bKey) ?? 0) + exceptionsForRow);
  }

  // A bucket with zero executions (idle) is unambiguously 0% failure - it must never be computed
  // from, or carry forward, whatever the previous bucket's failure rate happened to be.
  return bucketStarts
    .map(t => {
      const totalRequests = requestsByBucket.get(t) ?? 0;
      if (totalRequests === 0) {
        return { time: new Date(t), failureRatePercent: 0 };
      }

      const totalExceptions = exceptionsByBucket.get(t) ?? 0;
      const failureRatePercent = (totalExceptions / totalRequests) * 100;
      return { time: new Date(t), failureRatePercent };
    })
    .sort((a, b) => a.time.getTime() - b.time.getTime());
}
