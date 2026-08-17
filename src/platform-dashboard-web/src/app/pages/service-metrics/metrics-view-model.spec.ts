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
    expect(vm.overview.p50).toBe(0);
    expect(vm.overview.p95).toBe(0);
    expect(vm.overview.p99).toBe(0);
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
});
