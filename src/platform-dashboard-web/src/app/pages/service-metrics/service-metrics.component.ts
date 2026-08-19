import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTableModule } from '@angular/material/table';
import { BaseChartDirective } from 'ng2-charts';
import { ChartConfiguration, ChartOptions } from 'chart.js';
import 'chartjs-adapter-date-fns';
import { PlatformApiService } from '../../core/platform-api.service';
import { MetricPoint, RequestTypeRankingItem, ServiceSummary, TimeRange } from '../../core/models';
import { buildMetricsViewModel, MetricsViewModel } from './metrics-view-model';
import { AxisBounds, formatTickLabel, formatTooltipTimestamp, getAxisBounds, getRangeBounds } from './chart-time-axis';

// Chart library: ng2-charts (Chart.js wrapper). Chosen over ngx-charts/ApexCharts because it has
// the smallest footprint, integrates trivially with standalone components via BaseChartDirective,
// and Chart.js exposes simple per-chart color/grid/tooltip options that are easy to theme for a
// dark-only UI without pulling in a heavier charting framework.
// A `time` x-axis scale (via `chartjs-adapter-date-fns`) is used instead of a `category` scale so
// the axis represents a genuine continuous time domain with an explicit min/max, rather than
// shrinking to fit whatever sparse data points happen to exist.
const DARK_GRID_COLOR = 'rgba(255, 255, 255, 0.08)';
const DARK_TEXT_COLOR = 'rgba(255, 255, 255, 0.7)';
const DARK_TOOLTIP_BG = '#2a2f3a';

const DEFAULT_PAGE_SIZE = 20;

@Component({
  selector: 'app-service-metrics',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    MatButtonModule,
    MatButtonToggleModule,
    MatCardModule,
    MatIconModule,
    MatPaginatorModule,
    MatProgressSpinnerModule,
    MatTableModule,
    BaseChartDirective
  ],
  templateUrl: './service-metrics.component.html',
  styleUrl: './service-metrics.component.scss'
})
export class ServiceMetricsComponent implements OnInit {
  service: ServiceSummary | undefined;
  range: TimeRange = '1h';
  loading = true;
  error = false;
  viewModel: MetricsViewModel | undefined;

  // Request-types table state (server-side paginated, independent of the rest of the page).
  rankingRows: RequestTypeRankingItem[] = [];
  rankingLoading = true;
  rankingError = false;
  rankingPage = 0; // zero-based, as used by mat-paginator
  rankingPageSize = DEFAULT_PAGE_SIZE;
  rankingTotalItems = 0;
  readonly rankingPageSizeOptions = [20, 50, 100];
  readonly rankingColumns = ['requestType', 'executionCount', 'p95', 'p99', 'failurePercent'];

  tenantId = '';
  private serviceId = '';

  private axisBounds!: AxisBounds;

  readonly baseChartOptions = (): ChartOptions<'line'> => ({
    responsive: true,
    maintainAspectRatio: false,
    interaction: { mode: 'index', intersect: false },
    scales: {
      x: {
        type: 'time',
        min: this.axisBounds.min,
        max: this.axisBounds.max,
        time: { unit: this.axisBounds.unit },
        ticks: {
          color: DARK_TEXT_COLOR,
          maxRotation: 0,
          autoSkip: true,
          maxTicksLimit: this.axisBounds.maxTicksLimit,
          stepSize: this.axisBounds.stepSize,
          source: 'auto',
          callback: (value: unknown) => formatTickLabel(new Date(value as number), this.range)
        },
        grid: { color: DARK_GRID_COLOR }
      },
      y: {
        ticks: { color: DARK_TEXT_COLOR },
        grid: { color: DARK_GRID_COLOR },
        beginAtZero: true
      }
    },
    plugins: {
      legend: {
        labels: { color: DARK_TEXT_COLOR }
      },
      tooltip: {
        backgroundColor: DARK_TOOLTIP_BG,
        titleColor: '#ffffff',
        bodyColor: '#ffffff',
        callbacks: {
          title: (items) => (items.length ? formatTooltipTimestamp(new Date(items[0].parsed.x ?? 0)) : '')
        }
      }
    }
  });

  throughputChartOptions: ChartOptions<'line'> = {};
  latencyChartOptions: ChartOptions<'line'> = {};
  failureRateChartOptions: ChartOptions<'line'> = {};

  throughputChartData: ChartConfiguration<'line'>['data'] = { labels: [], datasets: [] };
  latencyChartData: ChartConfiguration<'line'>['data'] = { labels: [], datasets: [] };
  failureRateChartData: ChartConfiguration<'line'>['data'] = { labels: [], datasets: [] };

  constructor(
    private readonly route: ActivatedRoute,
    private readonly api: PlatformApiService
  ) {}

  ngOnInit(): void {
    this.tenantId = this.route.snapshot.paramMap.get('tenantId') ?? '';
    this.serviceId = this.route.snapshot.paramMap.get('id') ?? '';
    this.api.getServiceById(this.tenantId, this.serviceId).subscribe(service => (this.service = service));
    this.loadMetrics();
    this.loadRanking();
  }

  selectRange(range: TimeRange): void {
    if (range === this.range) {
      return;
    }
    this.range = range;
    this.rankingPage = 0;
    this.loadMetrics();
    this.loadRanking();
  }

  onRankingPageChange(event: PageEvent): void {
    this.rankingPage = event.pageIndex;
    this.rankingPageSize = event.pageSize;
    this.loadRanking();
  }

  private loadMetrics(): void {
    this.loading = true;
    this.error = false;
    // Reset any previously rendered data so switching ranges never leaves stale cards/charts visible.
    this.viewModel = undefined;
    this.resetCharts();

    // Computed once per data-load: the range's fixed [start, now] window, which the view model uses
    // to build a bucket grid that always spans through to the current time - not just to wherever
    // the last received metric happens to be - and which the chart axis then renders against.
    const now = new Date();
    const { start } = getRangeBounds(this.range, now);
    this.axisBounds = getAxisBounds(this.range, now);

    this.api.getServiceMetricsRaw(this.tenantId, this.serviceId, this.range).subscribe({
      next: (points: MetricPoint[]) => {
        this.viewModel = buildMetricsViewModel(points, { rangeStart: start, rangeEnd: now });
        this.renderCharts(this.viewModel);
        this.loading = false;
      },
      error: () => {
        this.viewModel = undefined;
        this.error = true;
        this.loading = false;
      }
    });
  }

  private loadRanking(): void {
    this.rankingLoading = true;
    this.rankingError = false;

    // mat-paginator page index is zero-based; the API is one-based.
    const page = this.rankingPage + 1;
    this.api.getRequestTypeRanking(this.tenantId, this.serviceId, this.range, page, this.rankingPageSize).subscribe({
      next: response => {
        this.rankingRows = response.items;
        this.rankingTotalItems = response.totalItems;
        this.rankingLoading = false;
      },
      error: () => {
        this.rankingRows = [];
        this.rankingError = true;
        this.rankingLoading = false;
      }
    });
  }

  private resetCharts(): void {
    this.throughputChartData = { labels: [], datasets: [] };
    this.latencyChartData = { labels: [], datasets: [] };
    this.failureRateChartData = { labels: [], datasets: [] };
  }

  private renderCharts(vm: MetricsViewModel): void {
    // axisBounds was already computed once in loadMetrics (shared with the view-model's bucket
    // grid), so every chart on the page shares an identical axis domain.
    const chartOptions = this.baseChartOptions();
    this.throughputChartOptions = chartOptions;
    this.latencyChartOptions = chartOptions;
    this.failureRateChartOptions = chartOptions;

    this.throughputChartData = {
      datasets: [
        {
          label: 'Requests',
          data: vm.throughput.map(p => ({ x: p.time.getTime(), y: p.count })),
          borderColor: '#4fc3f7',
          backgroundColor: 'rgba(79, 195, 247, 0.2)',
          fill: true,
          tension: 0.25,
          // Throughput has no gaps (0 executions is a real value for every bucket), but spanGaps is
          // set explicitly here for consistency with the other two charts.
          spanGaps: false
        }
      ]
    };

    // p50/p95/p99 are null for buckets with no duration observations - spanGaps: false ensures
    // Chart.js renders those as a visible break in the line instead of interpolating/holding the
    // previous value across the gap.
    this.latencyChartData = {
      datasets: [
        {
          label: 'P50 (ms)',
          data: vm.latency.map(p => ({ x: p.time.getTime(), y: p.p50 })),
          borderColor: '#81c784',
          tension: 0.25,
          spanGaps: false
        },
        {
          label: 'P95 (ms)',
          data: vm.latency.map(p => ({ x: p.time.getTime(), y: p.p95 })),
          borderColor: '#ffb74d',
          tension: 0.25,
          spanGaps: false
        },
        {
          label: 'P99 (ms)',
          data: vm.latency.map(p => ({ x: p.time.getTime(), y: p.p99 })),
          borderColor: '#e57373',
          tension: 0.25,
          spanGaps: false
        }
      ]
    };

    // failureRatePercent is null for buckets with no executions - spanGaps: false ensures Chart.js
    // renders those as a visible break in the line instead of interpolating/holding the previous
    // failure rate across the gap (the exact bug this fix addresses).
    this.failureRateChartData = {
      datasets: [
        {
          label: 'Failure rate (%)',
          data: vm.failureRate.map(p => ({ x: p.time.getTime(), y: p.failureRatePercent })),
          borderColor: '#e57373',
          backgroundColor: 'rgba(229, 115, 115, 0.2)',
          fill: true,
          tension: 0.25,
          spanGaps: false
        }
      ]
    };
  }
}
