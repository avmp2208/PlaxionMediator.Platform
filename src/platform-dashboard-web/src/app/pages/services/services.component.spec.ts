import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { ServicesComponent } from './services.component';
import { PlatformApiService } from '../../core/platform-api.service';

describe('ServicesComponent', () => {
  let component: ServicesComponent;
  let fixture: ComponentFixture<ServicesComponent>;

  beforeEach(async () => {
    const apiMock = {
      getServices: () =>
        of([
          { id: 's1', name: 'billing-api', environment: 'production' },
          { id: 's2', name: 'orders-api', environment: 'staging' }
        ])
    };

    await TestBed.configureTestingModule({
      imports: [ServicesComponent],
      providers: [
        provideRouter([]),
        { provide: PlatformApiService, useValue: apiMock }
      ]
    })
    .compileComponents();

    fixture = TestBed.createComponent(ServicesComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('still lists services for Member users (no invitation management UI)', () => {
    const text = (fixture.nativeElement as HTMLElement).textContent || '';
    expect(text).toContain('billing-api');
    expect(text).toContain('orders-api');
    expect(text.toLowerCase()).not.toContain('invite');
    expect(text.toLowerCase()).not.toContain('revoke');
    expect(text.toLowerCase()).not.toContain('api key');
  });
});
