import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { AppConfigService } from './app-config.service';
import { environment } from '../../environments/environment';

describe('AppConfigService', () => {
  let service: AppConfigService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [AppConfigService]
    });
    service = TestBed.inject(AppConfigService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should fallback to environment.apiBaseUrl when config.json is missing', async () => {
    const promise = service.loadConfig();
    const req = httpMock.expectOne('assets/config.json');
    req.error(new ProgressEvent('error'), { status: 404 });
    
    await promise;
    expect(service.apiBaseUrl).toBe(environment.apiBaseUrl);
  });

  it('should fallback to environment.apiBaseUrl when apiBaseUrl key is missing in config.json', async () => {
    const promise = service.loadConfig();
    const req = httpMock.expectOne('assets/config.json');
    req.flush({});
    
    await promise;
    expect(service.apiBaseUrl).toBe(environment.apiBaseUrl);
  });

  it('should use the empty apiBaseUrl from config.json (same-origin) instead of falling back', async () => {
    // An explicitly empty apiBaseUrl is a deliberate "same-origin" instruction from the
    // container entrypoint and must NOT fall back to the compiled environment default.
    const promise = service.loadConfig();
    const req = httpMock.expectOne('assets/config.json');
    req.flush({ apiBaseUrl: '' });

    await promise;
    expect(service.apiBaseUrl).toBe('');
  });

  it('should use apiBaseUrl from config.json when present', async () => {
    const promise = service.loadConfig();
    const req = httpMock.expectOne('assets/config.json');
    req.flush({ apiBaseUrl: 'http://custom-api:8080' });
    
    await promise;
    expect(service.apiBaseUrl).toBe('http://custom-api:8080');
  });
});
