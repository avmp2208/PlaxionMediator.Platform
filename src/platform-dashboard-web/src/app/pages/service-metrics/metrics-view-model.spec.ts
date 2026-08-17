import { MetricPoint } from '../../core/models';
import { buildMetricsViewModel } from './metrics-view-model';

function point(overrides: Partial<MetricPoint>): MetricPoint {
  return {
    bucketStart: '2024-01-01T00:00:00.000Z',
    bucketEnd: '2024-01-01T00:05:00.000Z',
    metricName: 'plaxionmediator.request.count',
    count: 0,
    min: 0,
    max: 0,
    avg: 0,
    p50: 0,
    p95: 0,
    p99: 0,
    requestType: null,
    ...overrides
  };
}

describe('buildMetricsViewModel', () => {
  it('returns sensible empty/zero results for empty input', () => {
    const vm = buildMetricsViewModel([]);

    expect(vm.overview.totalExecutions).toBe(0);
    expect(vm.overview.p50).toBeNull();
    expect(vm.overview.p95).toBeNull();
    expect(vm.overview.p99).toBeNull();
    expect(vm.overview.failurePercent).toBe(0);
    expect(vm.overview.distinctRequestTypeCount).toBe(0);
    expect(vm.throughput).toEqual([]);
    expect(vm.latency).toEqual([]);
    expect(vm.failureRate).toEqual([]);
    expect(vm.hasFailureData).toBeFalse();
  });

  it('computes correct overview/series/ranking for a single requestType', () => {
    const bucket = '2024-01-01T00:00:00.000Z';
    const points: MetricPoint[] = [
      point({
        metricName: 'plaxionmediator.request.count',
        requestType: 'MyApp.PingRequest',
        bucketStart: bucket,
        count: 100
      }),
      point({
        metricName: 'plaxionmediator.request.duration',
        requestType: 'MyApp.PingRequest',
        bucketStart: bucket,
        count: 100,
        p50: 10,
        p95: 20,
        p99: 30
      }),
      point({
        metricName: 'plaxionmediator.pipeline_exception.count',
        requestType: 'MyApp.PingRequest',
        bucketStart: bucket,
        count: 5
      })
    ];

    const vm = buildMetricsViewModel(points);

    expect(vm.overview.totalExecutions).toBe(100);
    expect(vm.overview.p50).toBe(10);
    expect(vm.overview.p95).toBe(20);
    expect(vm.overview.p99).toBe(30);
    expect(vm.overview.failurePercent).toBe(5);
    expect(vm.overview.distinctRequestTypeCount).toBe(1);

    expect(vm.throughput.length).toBe(1);
    expect(vm.throughput[0].count).toBe(100);

    expect(vm.latency.length).toBe(1);
    expect(vm.latency[0].p50).toBe(10);
    expect(vm.latency[0].p95).toBe(20);
    expect(vm.latency[0].p99).toBe(30);

    expect(vm.failureRate.length).toBe(1);
    expect(vm.failureRate[0].failureRatePercent).toBe(5);

    expect(vm.hasFailureData).toBeTrue();
  });

  it('correctly splits/aggregates multiple requestTypes', () => {
    const bucket = '2024-01-01T00:00:00.000Z';
    const points: MetricPoint[] = [
      point({ metricName: 'plaxionmediator.request.count', requestType: 'TypeA', bucketStart: bucket, count: 100 }),
      point({ metricName: 'plaxionmediator.request.count', requestType: 'TypeB', bucketStart: bucket, count: 50 }),
      point({
        metricName: 'plaxionmediator.request.duration',
        requestType: 'TypeA',
        bucketStart: bucket,
        count: 100,
        p50: 10,
        p95: 20,
        p99: 30
      }),
      point({
        metricName: 'plaxionmediator.request.duration',
        requestType: 'TypeB',
        bucketStart: bucket,
        count: 50,
        p50: 40,
        p95: 50,
        p99: 60
      })
    ];

    const vm = buildMetricsViewModel(points);

    expect(vm.overview.totalExecutions).toBe(150);
    expect(vm.overview.distinctRequestTypeCount).toBe(2);
    // count-weighted average: (10*100 + 40*50)/150 = 20
    expect(vm.overview.p50).toBeCloseTo(20, 5);

    expect(vm.throughput.length).toBe(1);
    expect(vm.throughput[0].count).toBe(150);
  });

  it('treats a bucket with request.count but no matching exception row as 0 failure rate', () => {
    const bucket = '2024-01-01T00:00:00.000Z';
    const points: MetricPoint[] = [
      point({ metricName: 'plaxionmediator.request.count', requestType: 'TypeA', bucketStart: bucket, count: 100 })
    ];

    const vm = buildMetricsViewModel(points);

    expect(vm.overview.failurePercent).toBe(0);
    expect(vm.failureRate.length).toBe(1);
    expect(vm.failureRate[0].failureRatePercent).toBe(0);
    expect(vm.hasFailureData).toBeFalse();
  });

  it('excludes metrics with requestType null from ranking and does not crash', () => {
    const bucket = '2024-01-01T00:00:00.000Z';
    const points: MetricPoint[] = [
      point({ metricName: 'some.generic.metric', requestType: null, bucketStart: bucket, count: 10 }),
      point({ metricName: 'plaxionmediator.request.count', requestType: 'TypeA', bucketStart: bucket, count: 5 })
    ];

    expect(() => buildMetricsViewModel(points)).not.toThrow();

    const vm = buildMetricsViewModel(points);
    expect(vm.overview.totalExecutions).toBe(5);
  });

  it('recomputes failure rate to 0% once a bucket with only successful executions follows a 70% failure bucket', () => {
    const bucketA = '2024-01-01T00:00:00.000Z';
    const bucketB = '2024-01-01T00:05:00.000Z';
    const points: MetricPoint[] = [
      // Bucket A: 10 executions, 7 failures -> 70% failure rate.
      point({ metricName: 'plaxionmediator.request.count', requestType: 'TypeA', bucketStart: bucketA, bucketEnd: bucketB, count: 10 }),
      point({ metricName: 'plaxionmediator.pipeline_exception.count', requestType: 'TypeA', bucketStart: bucketA, bucketEnd: bucketB, count: 7 }),
      // Bucket B: 10 executions, 0 failures -> must be computed independently as 0%, not 70%.
      point({ metricName: 'plaxionmediator.request.count', requestType: 'TypeA', bucketStart: bucketB, bucketEnd: '2024-01-01T00:10:00.000Z', count: 10 })
    ];

    const vm = buildMetricsViewModel(points);

    expect(vm.failureRate.length).toBe(2);
    expect(vm.failureRate[0].failureRatePercent).toBe(70);
    expect(vm.failureRate[1].failureRatePercent).toBe(0);
  });

  it('reports 0% (idle), not the previous value, for a bucket with no executions after a 70% failure bucket', () => {
    const bucketA = '2024-01-01T00:00:00.000Z';
    const bucketB = '2024-01-01T00:05:00.000Z';
    const bucketC = '2024-01-01T00:10:00.000Z';
    const points: MetricPoint[] = [
      // Bucket A: 10 executions, 7 failures -> 70% failure rate.
      point({ metricName: 'plaxionmediator.request.count', requestType: 'TypeA', bucketStart: bucketA, bucketEnd: bucketB, count: 10 }),
      point({ metricName: 'plaxionmediator.pipeline_exception.count', requestType: 'TypeA', bucketStart: bucketA, bucketEnd: bucketB, count: 7 }),
      // Bucket C has executions again, so the grid spans A..C and bucket B (no data at all - idle)
      // must appear as an explicit 0%, independently computed, never carrying 70% forward.
      point({ metricName: 'plaxionmediator.request.count', requestType: 'TypeA', bucketStart: bucketC, bucketEnd: '2024-01-01T00:15:00.000Z', count: 10 })
    ];

    const vm = buildMetricsViewModel(points);

    expect(vm.failureRate.length).toBe(3);
    expect(vm.failureRate[0].failureRatePercent).toBe(70);
    expect(vm.failureRate[1].failureRatePercent).toBe(0);
    expect(vm.failureRate[2].failureRatePercent).toBe(0);

    // Throughput treats the same idle bucket as a valid 0, not a gap.
    expect(vm.throughput.length).toBe(3);
    expect(vm.throughput[1].count).toBe(0);

    // No duration rows at all in this scenario -> every bucket in the shared grid is a null gap
    // for latency specifically (0ms would incorrectly imply an actual measurement), never carried forward.
    expect(vm.latency.length).toBe(3);
    expect(vm.latency.every(l => l.p50 === null && l.p95 === null && l.p99 === null)).toBeTrue();
  });

  it('idle buckets following real traffic report throughput 0, failure rate 0%, and null latency (never carried forward)', () => {
    // 12:00 -> 10 executions, 70% failures, P95 500ms
    // 12:01 -> 10 executions, 20% failures, P95 300ms
    // 12:02, 12:03 -> idle (no executions at all)
    const t0 = '2024-01-01T12:00:00.000Z';
    const t1 = '2024-01-01T12:01:00.000Z';
    const t2 = '2024-01-01T12:02:00.000Z';
    const t3 = '2024-01-01T12:03:00.000Z';
    const t4 = '2024-01-01T12:04:00.000Z';
    const points: MetricPoint[] = [
      point({ metricName: 'plaxionmediator.request.count', requestType: 'TypeA', bucketStart: t0, bucketEnd: t1, count: 10 }),
      point({ metricName: 'plaxionmediator.pipeline_exception.count', requestType: 'TypeA', bucketStart: t0, bucketEnd: t1, count: 7 }),
      point({ metricName: 'plaxionmediator.request.duration', requestType: 'TypeA', bucketStart: t0, bucketEnd: t1, count: 10, p50: 400, p95: 500, p99: 550 }),
      point({ metricName: 'plaxionmediator.request.count', requestType: 'TypeA', bucketStart: t1, bucketEnd: t2, count: 10 }),
      point({ metricName: 'plaxionmediator.pipeline_exception.count', requestType: 'TypeA', bucketStart: t1, bucketEnd: t2, count: 2 }),
      point({ metricName: 'plaxionmediator.request.duration', requestType: 'TypeA', bucketStart: t1, bucketEnd: t2, count: 10, p50: 250, p95: 300, p99: 320 }),
      // A trailing marker bucket at t4 so the grid spans through the two idle buckets t2/t3.
      point({ metricName: 'plaxionmediator.request.count', requestType: 'TypeA', bucketStart: t4, bucketEnd: '2024-01-01T12:05:00.000Z', count: 0 })
    ];

    const vm = buildMetricsViewModel(points);

    expect(vm.throughput.map(p => p.count)).toEqual([10, 10, 0, 0, 0]);
    expect(vm.failureRate.map(p => p.failureRatePercent)).toEqual([70, 20, 0, 0, 0]);
    expect(vm.latency[2].p50).toBeNull();
    expect(vm.latency[2].p95).toBeNull();
    expect(vm.latency[3].p95).toBeNull();
  });

  it('idle bucket sandwiched between two traffic periods resets failure rate to 0% and never repeats the previous latency', () => {
    const t0 = '2024-01-01T00:00:00.000Z';
    const t1 = '2024-01-01T00:01:00.000Z';
    const t2 = '2024-01-01T00:02:00.000Z';
    const points: MetricPoint[] = [
      point({ metricName: 'plaxionmediator.request.count', requestType: 'TypeA', bucketStart: t0, bucketEnd: t1, count: 5 }),
      point({ metricName: 'plaxionmediator.pipeline_exception.count', requestType: 'TypeA', bucketStart: t0, bucketEnd: t1, count: 5 }),
      point({ metricName: 'plaxionmediator.request.duration', requestType: 'TypeA', bucketStart: t0, bucketEnd: t1, count: 5, p50: 900, p95: 950, p99: 999 }),
      // Traffic resumes at t2 with zero failures - proves resumption is computed independently.
      point({ metricName: 'plaxionmediator.request.count', requestType: 'TypeA', bucketStart: t2, bucketEnd: '2024-01-01T00:03:00.000Z', count: 8 })
    ];

    const vm = buildMetricsViewModel(points);

    expect(vm.failureRate.map(p => p.failureRatePercent)).toEqual([100, 0, 0]);
    expect(vm.throughput.map(p => p.count)).toEqual([5, 0, 8]);
    expect(vm.latency[1].p50).toBeNull();
    expect(vm.latency[1].p95).toBeNull();
    expect(vm.latency[1].p99).toBeNull();
  });

  it('never lets latency carry forward into a bucket with no duration observations', () => {
    const bucketA = '2024-01-01T00:00:00.000Z';
    const bucketB = '2024-01-01T00:05:00.000Z';
    const bucketC = '2024-01-01T00:10:00.000Z';
    const points: MetricPoint[] = [
      point({
        metricName: 'plaxionmediator.request.duration',
        requestType: 'TypeA',
        bucketStart: bucketA,
        bucketEnd: bucketB,
        count: 10,
        p50: 100,
        p95: 200,
        p99: 300
      }),
      point({
        metricName: 'plaxionmediator.request.duration',
        requestType: 'TypeA',
        bucketStart: bucketC,
        bucketEnd: '2024-01-01T00:15:00.000Z',
        count: 10,
        p50: 5,
        p95: 6,
        p99: 7
      })
    ];

    const vm = buildMetricsViewModel(points);

    expect(vm.latency.length).toBe(3);
    expect(vm.latency[0].p50).toBe(100);
    expect(vm.latency[1].p50).toBeNull();
    expect(vm.latency[1].p95).toBeNull();
    expect(vm.latency[1].p99).toBeNull();
    expect(vm.latency[2].p50).toBe(5);
  });

  describe('range extension to current time', () => {
    it('extends the chart domain all the way to "now" for Last Hour, even 15+ minutes after the last received metric', () => {
      const now = new Date('2024-01-01T13:31:00.000Z');
      const rangeStart = new Date(now.getTime() - 60 * 60 * 1000); // 12:31
      const t0 = '2024-01-01T13:14:00.000Z';
      const t1 = '2024-01-01T13:15:00.000Z';
      const points: MetricPoint[] = [
        point({ metricName: 'plaxionmediator.request.count', requestType: 'TypeA', bucketStart: t0, bucketEnd: t1, count: 10 }),
        point({ metricName: 'plaxionmediator.request.count', requestType: 'TypeA', bucketStart: t1, bucketEnd: '2024-01-01T13:16:00.000Z', count: 10 })
      ];

      const vm = buildMetricsViewModel(points, { rangeStart, rangeEnd: now });

      // The chart must keep going all the way to "now" (13:31), not stop at the last real
      // data point (13:16) - i.e. 15+ idle minutes must still render as trailing 0/0%/null buckets.
      const lastBucket = vm.throughput[vm.throughput.length - 1];
      expect(lastBucket.time.getTime()).toBe(now.getTime());
      expect(lastBucket.count).toBe(0);

      const lastFailure = vm.failureRate[vm.failureRate.length - 1];
      expect(lastFailure.failureRatePercent).toBe(0);

      const lastLatency = vm.latency[vm.latency.length - 1];
      expect(lastLatency.p50).toBeNull();
      expect(lastLatency.p95).toBeNull();
      expect(lastLatency.p99).toBeNull();

      // Throughput for the two real minutes (13:14, 13:15), then idle (0) all the way through to "now".
      const at = (iso: string) => vm.throughput.find(p => p.time.getTime() === new Date(iso).getTime());
      expect(at(t0)?.count).toBe(10);
      expect(at(t1)?.count).toBe(10);
      expect(vm.throughput.filter(p => p.time.getTime() > new Date(t1).getTime()).every(p => p.count === 0)).toBeTrue();

      // The domain also starts exactly at the range start (12:31), regardless of when the first
      // real data point happened to be.
      expect(vm.throughput[0].time.getTime()).toBe(rangeStart.getTime());
    });

    it('extends the chart domain to "now" for Last 24 Hours', () => {
      const now = new Date('2024-01-05T00:00:00.000Z');
      const rangeStart = new Date(now.getTime() - 24 * 60 * 60 * 1000);
      const points: MetricPoint[] = [
        point({
          metricName: 'plaxionmediator.request.count',
          requestType: 'TypeA',
          bucketStart: rangeStart.toISOString(),
          bucketEnd: new Date(rangeStart.getTime() + 60_000).toISOString(),
          count: 4
        })
      ];

      const vm = buildMetricsViewModel(points, { rangeStart, rangeEnd: now });

      const lastBucket = vm.throughput[vm.throughput.length - 1];
      expect(lastBucket.time.getTime()).toBe(now.getTime());
      expect(lastBucket.count).toBe(0);
      expect(vm.failureRate[vm.failureRate.length - 1].failureRatePercent).toBe(0);
      expect(vm.latency[vm.latency.length - 1].p50).toBeNull();
    });

    it('extends the chart domain to "now" for Last 7 Days', () => {
      const now = new Date('2024-01-08T00:00:00.000Z');
      const rangeStart = new Date(now.getTime() - 7 * 24 * 60 * 60 * 1000);
      const points: MetricPoint[] = [
        point({
          metricName: 'plaxionmediator.request.count',
          requestType: 'TypeA',
          bucketStart: rangeStart.toISOString(),
          bucketEnd: new Date(rangeStart.getTime() + 60_000).toISOString(),
          count: 3
        })
      ];

      const vm = buildMetricsViewModel(points, { rangeStart, rangeEnd: now });

      const lastBucket = vm.throughput[vm.throughput.length - 1];
      expect(lastBucket.time.getTime()).toBe(now.getTime());
      expect(lastBucket.count).toBe(0);
      expect(vm.failureRate[vm.failureRate.length - 1].failureRatePercent).toBe(0);
      expect(vm.latency[vm.latency.length - 1].p50).toBeNull();
    });

    it('still extends to "now" when there is no data at all in the selected range', () => {
      const now = new Date('2024-01-01T13:31:00.000Z');
      const rangeStart = new Date(now.getTime() - 60 * 60 * 1000);

      const vm = buildMetricsViewModel([], { rangeStart, rangeEnd: now });

      expect(vm.throughput.length).toBeGreaterThan(0);
      expect(vm.throughput.every(p => p.count === 0)).toBeTrue();
      expect(vm.failureRate.every(p => p.failureRatePercent === 0)).toBeTrue();
      expect(vm.latency.every(p => p.p50 === null && p.p95 === null && p.p99 === null)).toBeTrue();
      expect(vm.throughput[vm.throughput.length - 1].time.getTime()).toBe(now.getTime());
      expect(vm.throughput[0].time.getTime()).toBe(rangeStart.getTime());
    });
  });

  describe('shared bucket timeline across throughput/latency/failureRate', () => {
    it('produces identical timestamp arrays for all three charts, with latency null on idle buckets, for Last Hour', () => {
      const now = new Date('2024-01-01T13:31:00.000Z');
      const rangeStart = new Date(now.getTime() - 60 * 60 * 1000);
      const t0 = '2024-01-01T13:14:00.000Z';
      const t1 = '2024-01-01T13:15:00.000Z';
      const points: MetricPoint[] = [
        point({ metricName: 'plaxionmediator.request.count', requestType: 'TypeA', bucketStart: t0, bucketEnd: t1, count: 10 }),
        point({
          metricName: 'plaxionmediator.request.duration',
          requestType: 'TypeA',
          bucketStart: t0,
          bucketEnd: t1,
          count: 10,
          p50: 92,
          p95: 100,
          p99: 110
        })
      ];

      const vm = buildMetricsViewModel(points, { rangeStart, rangeEnd: now });

      const throughputTimes = vm.throughput.map(p => p.time.getTime());
      const latencyTimes = vm.latency.map(p => p.time.getTime());
      const failureTimes = vm.failureRate.map(p => p.time.getTime());

      // All three charts must use the exact same rangeStart, rangeEnd, and bucket boundaries.
      expect(latencyTimes).toEqual(throughputTimes);
      expect(failureTimes).toEqual(throughputTimes);
      expect(throughputTimes.length).toBeGreaterThan(1);

      // The bucket that actually had a duration observation reports real latency values...
      const t0Index = throughputTimes.indexOf(new Date(t0).getTime());
      expect(vm.latency[t0Index].p50).toBe(92);

      // ...while every other bucket (idle) reports null, never a carried-forward value.
      vm.latency.forEach((p, index) => {
        if (index !== t0Index) {
          expect(p.p50).toBeNull();
          expect(p.p95).toBeNull();
          expect(p.p99).toBeNull();
        }
      });
    });

    it('produces identical timestamp arrays for all three charts for Last 24 Hours', () => {
      const now = new Date('2024-01-05T00:00:00.000Z');
      const rangeStart = new Date(now.getTime() - 24 * 60 * 60 * 1000);
      const points: MetricPoint[] = [
        point({
          metricName: 'plaxionmediator.request.duration',
          requestType: 'TypeA',
          bucketStart: rangeStart.toISOString(),
          bucketEnd: new Date(rangeStart.getTime() + 60_000).toISOString(),
          count: 4,
          p50: 84
        })
      ];

      const vm = buildMetricsViewModel(points, { rangeStart, rangeEnd: now });

      const throughputTimes = vm.throughput.map(p => p.time.getTime());
      const latencyTimes = vm.latency.map(p => p.time.getTime());
      const failureTimes = vm.failureRate.map(p => p.time.getTime());

      expect(latencyTimes).toEqual(throughputTimes);
      expect(failureTimes).toEqual(throughputTimes);
    });

    it('produces identical timestamp arrays for all three charts for Last 7 Days', () => {
      const now = new Date('2024-01-08T00:00:00.000Z');
      const rangeStart = new Date(now.getTime() - 7 * 24 * 60 * 60 * 1000);
      const points: MetricPoint[] = [
        point({
          metricName: 'plaxionmediator.request.duration',
          requestType: 'TypeA',
          bucketStart: rangeStart.toISOString(),
          bucketEnd: new Date(rangeStart.getTime() + 60_000).toISOString(),
          count: 3,
          p50: 77
        })
      ];

      const vm = buildMetricsViewModel(points, { rangeStart, rangeEnd: now });

      const throughputTimes = vm.throughput.map(p => p.time.getTime());
      const latencyTimes = vm.latency.map(p => p.time.getTime());
      const failureTimes = vm.failureRate.map(p => p.time.getTime());

      expect(latencyTimes).toEqual(throughputTimes);
      expect(failureTimes).toEqual(throughputTimes);
    });
  });

  describe('zero-traffic ranges (Remove the Empty Dashboard State)', () => {
    it('a zero-traffic Last Hour still produces a full, zero-filled/null-filled view model - not an empty result', () => {
      const now = new Date('2024-01-01T13:31:00.000Z');
      const rangeStart = new Date(now.getTime() - 60 * 60 * 1000);

      const vm = buildMetricsViewModel([], { rangeStart, rangeEnd: now });

      // Summary semantics: 0 executions, — (null) latency, 0% failure rate, 0 request types.
      expect(vm.overview.totalExecutions).toBe(0);
      expect(vm.overview.p50).toBeNull();
      expect(vm.overview.p95).toBeNull();
      expect(vm.overview.p99).toBeNull();
      expect(vm.overview.failurePercent).toBe(0);
      expect(vm.overview.distinctRequestTypeCount).toBe(0);

      // The complete time domain is still rendered, spanning rangeStart -> rangeEnd.
      expect(vm.throughput[0].time.getTime()).toBe(rangeStart.getTime());
      expect(vm.throughput[vm.throughput.length - 1].time.getTime()).toBe(now.getTime());
      expect(vm.throughput.length).toBeGreaterThan(1);

      // Throughput/failure-rate are zero-filled; latency is null-filled (never fabricated 0ms).
      expect(vm.throughput.every(p => p.count === 0)).toBeTrue();
      expect(vm.failureRate.every(p => p.failureRatePercent === 0)).toBeTrue();
      expect(vm.latency.every(p => p.p50 === null && p.p95 === null && p.p99 === null)).toBeTrue();
    });

    it('a zero-traffic Last 24 Hours still produces a full, zero-filled/null-filled view model', () => {
      const now = new Date('2024-01-05T00:00:00.000Z');
      const rangeStart = new Date(now.getTime() - 24 * 60 * 60 * 1000);

      const vm = buildMetricsViewModel([], { rangeStart, rangeEnd: now });

      expect(vm.overview.totalExecutions).toBe(0);
      expect(vm.overview.p50).toBeNull();
      expect(vm.overview.failurePercent).toBe(0);
      expect(vm.overview.distinctRequestTypeCount).toBe(0);
      expect(vm.throughput[0].time.getTime()).toBe(rangeStart.getTime());
      expect(vm.throughput[vm.throughput.length - 1].time.getTime()).toBe(now.getTime());
      expect(vm.throughput.every(p => p.count === 0)).toBeTrue();
      expect(vm.failureRate.every(p => p.failureRatePercent === 0)).toBeTrue();
      expect(vm.latency.every(p => p.p50 === null && p.p95 === null && p.p99 === null)).toBeTrue();
    });

    it('a zero-traffic Last 7 Days still produces a full, zero-filled/null-filled view model', () => {
      const now = new Date('2024-01-08T00:00:00.000Z');
      const rangeStart = new Date(now.getTime() - 7 * 24 * 60 * 60 * 1000);

      const vm = buildMetricsViewModel([], { rangeStart, rangeEnd: now });

      expect(vm.overview.totalExecutions).toBe(0);
      expect(vm.overview.p50).toBeNull();
      expect(vm.overview.failurePercent).toBe(0);
      expect(vm.overview.distinctRequestTypeCount).toBe(0);
      expect(vm.throughput[0].time.getTime()).toBe(rangeStart.getTime());
      expect(vm.throughput[vm.throughput.length - 1].time.getTime()).toBe(now.getTime());
      expect(vm.throughput.every(p => p.count === 0)).toBeTrue();
      expect(vm.failureRate.every(p => p.failureRatePercent === 0)).toBeTrue();
      expect(vm.latency.every(p => p.p50 === null && p.p95 === null && p.p99 === null)).toBeTrue();
    });
  });
});
