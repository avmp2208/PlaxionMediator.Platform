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

  /** Drain microtasks so async loadConfig can issue the next HTTP call. */
  async function tick(): Promise<void> {
    await Promise.resolve();
    await Promise.resolve();
  }

  async function completeAssetsFallback(assetsBody: object | 'error'): Promise<void> {
    const apiReq = httpMock.expectOne(`${environment.apiBaseUrl}/v1/platform/config`);
    apiReq.error(new ProgressEvent('error'), { status: 0 });
    await tick();

    const assetsReq = httpMock.expectOne('assets/config.json');
    if (assetsBody === 'error') {
      assetsReq.error(new ProgressEvent('error'), { status: 404 });
    } else {
      assetsReq.flush(assetsBody);
    }
    await tick();
  }

  it('should fallback to environment.apiBaseUrl when config sources are missing', async () => {
    const promise = service.loadConfig();
    await completeAssetsFallback('error');
    await promise;

    expect(service.apiBaseUrl).toBe(environment.apiBaseUrl);
    expect(service.authenticationEnabled).toBe(false);
  });

  it('should fallback to environment.apiBaseUrl when apiBaseUrl key is missing in config.json', async () => {
    const promise = service.loadConfig();

    const apiReq = httpMock.expectOne(`${environment.apiBaseUrl}/v1/platform/config`);
    apiReq.error(new ProgressEvent('error'), { status: 0 });
    await tick();

    const assetsReq = httpMock.expectOne('assets/config.json');
    assetsReq.flush({});
    await tick();

    // assets without authentication triggers a second API attempt
    const apiRetry = httpMock.expectOne(`${environment.apiBaseUrl}/v1/platform/config`);
    apiRetry.error(new ProgressEvent('error'), { status: 0 });
    await tick();

    await promise;
    expect(service.apiBaseUrl).toBe(environment.apiBaseUrl);
  });

  it('should use the empty apiBaseUrl from config.json (same-origin) instead of falling back', async () => {
    const promise = service.loadConfig();
    await completeAssetsFallback({ apiBaseUrl: '', authentication: { enabled: false } });
    await promise;

    expect(service.apiBaseUrl).toBe('');
  });

  it('should use apiBaseUrl from config.json when present', async () => {
    const promise = service.loadConfig();
    await completeAssetsFallback({
      apiBaseUrl: 'http://custom-api:8080',
      authentication: { enabled: false }
    });
    await promise;

    expect(service.apiBaseUrl).toBe('http://custom-api:8080');
  });

  it('should prefer the platform config endpoint when available', async () => {
    const promise = service.loadConfig();
    const apiReq = httpMock.expectOne(`${environment.apiBaseUrl}/v1/platform/config`);
    apiReq.flush({
      apiBaseUrl: 'https://api.from-endpoint',
      authentication: {
        enabled: true,
        authority: 'https://login.example/',
        clientId: 'dashboard',
        scopes: ['openid', 'profile', 'email']
      }
    });
    await tick();
    await promise;

    expect(service.apiBaseUrl).toBe('https://api.from-endpoint');
    expect(service.authenticationEnabled).toBe(true);
    expect(service.authentication.clientId).toBe('dashboard');
    expect(service.authentication.scopes).toEqual(['openid', 'profile', 'email']);
  });

  it('should expose authentication disabled by default from assets config', async () => {
    const promise = service.loadConfig();
    await completeAssetsFallback({
      apiBaseUrl: 'http://localhost:5095',
      authentication: { enabled: false, scopes: ['openid'] }
    });
    await promise;

    expect(service.authenticationEnabled).toBe(false);
    expect(service.authentication.scopes).toEqual(['openid']);
  });
});
