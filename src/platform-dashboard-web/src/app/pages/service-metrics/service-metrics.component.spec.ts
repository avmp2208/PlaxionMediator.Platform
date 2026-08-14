import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { ServiceMetricsComponent } from './service-metrics.component';
import { PlatformApiService } from '../../core/platform-api.service';

describe('ServiceMetricsComponent', () => {
  let component: ServiceMetricsComponent;
  let fixture: ComponentFixture<ServiceMetricsComponent>;

  beforeEach(async () => {
    const apiMock = {
      getServiceById: () => of(undefined),
      getServiceMetrics: () => of([])
    };

    await TestBed.configureTestingModule({
      imports: [ServiceMetricsComponent],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => 'test-id' } } }
        },
        { provide: PlatformApiService, useValue: apiMock }
      ]
    })
    .compileComponents();

    fixture = TestBed.createComponent(ServiceMetricsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
