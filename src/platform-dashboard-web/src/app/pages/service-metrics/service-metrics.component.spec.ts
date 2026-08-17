import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { provideCharts, withDefaultRegisterables } from 'ng2-charts';
import { provideNoopAnimations } from '@angular/platform-browser/animations';

import { ServiceMetricsComponent } from './service-metrics.component';
import { PlatformApiService } from '../../core/platform-api.service';
import { MetricPoint, PagedRequestTypeRankingResponse } from '../../core/models';

describe('ServiceMetricsComponent', () => {
  let component: ServiceMetricsComponent;
  let fixture: ComponentFixture<ServiceMetricsComponent>;
  let apiMock: {
    getServiceById: jasmine.Spy;
    getServiceMetricsRaw: jasmine.Spy;
    getRequestTypeRanking: jasmine.Spy;
  };

  const emptyRankingPage: PagedRequestTypeRankingResponse = {
    items: [],
    page: 1,
    pageSize: 20,
    totalItems: 0,
    totalPages: 0
  };

  function configure(): void {
    TestBed.configureTestingModule({
      imports: [ServiceMetricsComponent],
      providers: [
        provideRouter([]),
        provideCharts(withDefaultRegisterables()),
        provideNoopAnimations(),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => 'test-id' } } }
        },
        { provide: PlatformApiService, useValue: apiMock }
      ]
    });
  }

  beforeEach(() => {
    apiMock = {
      getServiceById: jasmine.createSpy().and.returnValue(of(undefined)),
      getServiceMetricsRaw: jasmine.createSpy().and.returnValue(of([])),
      getRequestTypeRanking: jasmine.createSpy().and.returnValue(of(emptyRankingPage))
    };
  });

  function createComponent(): void {
    configure();
    fixture = TestBed.createComponent(ServiceMetricsComponent);
    component = fixture.componentInstance;
  }

  it('should create', () => {
    createComponent();
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('shows loading state before data arrives', () => {
    apiMock.getServiceMetricsRaw.and.returnValue(of([]).pipe());
    createComponent();
    expect(component.loading).toBeTrue();
  });

  it('shows empty state when there are no requests in range', () => {
    createComponent();
    fixture.detectChanges();

    expect(component.loading).toBeFalse();
    expect(component.error).toBeFalse();
    expect(component.viewModel?.overview.totalExecutions).toBe(0);
  });

  it('shows error state when the API call fails', () => {
    apiMock.getServiceMetricsRaw.and.returnValue(throwError(() => new Error('boom')));
    createComponent();
    fixture.detectChanges();

    expect(component.loading).toBeFalse();
    expect(component.error).toBeTrue();
    expect(component.viewModel).toBeUndefined();
  });

  it('renders summary cards from mock API response', () => {
    const points: MetricPoint[] = [
      {
        bucketStart: '2024-01-01T00:00:00.000Z',
        bucketEnd: '2024-01-01T00:05:00.000Z',
        metricName: 'plaxionmediator.request.count',
        count: 10,
        min: 0,
        max: 0,
        avg: 0,
        p50: 0,
        p95: 0,
        p99: 0,
        requestType: 'TypeA'
      },
      {
        bucketStart: '2024-01-01T00:00:00.000Z',
        bucketEnd: '2024-01-01T00:05:00.000Z',
        metricName: 'plaxionmediator.request.duration',
        count: 10,
        min: 1,
        max: 5,
        avg: 3,
        p50: 3,
        p95: 4,
        p99: 5,
        requestType: 'TypeA'
      }
    ];
    apiMock.getServiceMetricsRaw.and.returnValue(of(points));
    createComponent();
    fixture.detectChanges();

    expect(component.loading).toBeFalse();
    expect(component.viewModel?.overview.totalExecutions).toBe(10);
    expect(component.viewModel?.hasFailureData).toBeFalse();
  });

  it('re-fetches and resets data when the time range changes', () => {
    createComponent();
    fixture.detectChanges();

    apiMock.getServiceMetricsRaw.calls.reset();
    apiMock.getRequestTypeRanking.calls.reset();
    component.selectRange('24h');

    expect(component.range).toBe('24h');
    expect(apiMock.getServiceMetricsRaw).toHaveBeenCalledWith('test-id', 'test-id', '24h');
    expect(apiMock.getRequestTypeRanking).toHaveBeenCalledWith('test-id', 'test-id', '24h', 1, 20);
    expect(component.rankingPage).toBe(0);
  });

  it('loads the request-types table with page=1/pageSize=20 by default', () => {
    createComponent();
    fixture.detectChanges();

    expect(apiMock.getRequestTypeRanking).toHaveBeenCalledWith('test-id', 'test-id', '1h', 1, 20);
  });

  it('requests the next page and updates the table when the paginator changes page', () => {
    const page1: PagedRequestTypeRankingResponse = {
      items: [{ requestType: 'TypeA', executionCount: 100, p95: 20, p99: 30, failurePercent: 0 }],
      page: 1,
      pageSize: 20,
      totalItems: 25,
      totalPages: 2
    };
    const page2: PagedRequestTypeRankingResponse = {
      items: [{ requestType: 'TypeB', executionCount: 5, p95: 10, p99: 15, failurePercent: 0 }],
      page: 2,
      pageSize: 20,
      totalItems: 25,
      totalPages: 2
    };
    apiMock.getRequestTypeRanking.and.returnValues(of(page1), of(page2));
    createComponent();
    fixture.detectChanges();

    expect(component.rankingRows[0].requestType).toBe('TypeA');

    component.onRankingPageChange({ pageIndex: 1, pageSize: 20, length: 25 });

    expect(apiMock.getRequestTypeRanking).toHaveBeenCalledWith('test-id', 'test-id', '1h', 2, 20);
    expect(component.rankingRows[0].requestType).toBe('TypeB');
  });

  it('shows a table-only error state when request-types fails but metrics succeeds', () => {
    apiMock.getRequestTypeRanking.and.returnValue(throwError(() => new Error('boom')));
    createComponent();
    fixture.detectChanges();

    expect(component.error).toBeFalse();
    expect(component.viewModel).toBeDefined();
    expect(component.rankingError).toBeTrue();
    expect(component.rankingLoading).toBeFalse();
  });
});
