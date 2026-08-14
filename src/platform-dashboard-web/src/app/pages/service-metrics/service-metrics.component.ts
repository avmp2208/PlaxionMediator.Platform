import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTableModule } from '@angular/material/table';
import { PlatformApiService } from '../../core/platform-api.service';
import { MetricSeries, ServiceSummary, TimeRange } from '../../core/models';

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
    MatProgressSpinnerModule,
    MatTableModule
  ],
  templateUrl: './service-metrics.component.html',
  styleUrl: './service-metrics.component.scss'
})
export class ServiceMetricsComponent implements OnInit {
  service: ServiceSummary | undefined;
  series: MetricSeries[] = [];
  range: TimeRange = '1h';
  loading = true;
  readonly displayedColumns = ['bucket', 'avg', 'p50', 'p95', 'p99', 'min', 'max', 'count'];
  private serviceId = '';

  constructor(
    private readonly route: ActivatedRoute,
    private readonly api: PlatformApiService
  ) {}

  ngOnInit(): void {
    this.serviceId = this.route.snapshot.paramMap.get('id') ?? '';
    this.api.getServiceById(this.serviceId).subscribe(service => (this.service = service));
    this.loadMetrics();
  }

  selectRange(range: TimeRange): void {
    if (range === this.range) {
      return;
    }
    this.range = range;
    this.loadMetrics();
  }

  private loadMetrics(): void {
    this.loading = true;
    this.api.getServiceMetrics(this.serviceId, this.range).subscribe({
      next: (series) => {
        this.series = series;
        this.loading = false;
      },
      error: () => {
        this.series = [];
        this.loading = false;
      }
    });
  }
}
