import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';

import { PlatformApiService } from './platform-api.service';
import { environment } from '../../environments/environment';

describe('PlatformApiService', () => {
  let service: PlatformApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule]
    });
    service = TestBed.inject(PlatformApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('should POST to /v1/tenants when creating a tenant', () => {
    const response = {
      tenantId: 't1',
      tenantName: 'Acme',
      apiKeyId: 'k1',
      apiKeyName: 'default',
      plaintextKey: 'pk_abcd1234',
      createdAt: '2024-01-01T00:00:00Z'
    };

    service.createTenant('Acme').subscribe((res) => expect(res).toEqual(response));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/tenants`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ name: 'Acme' });
    req.flush(response);
  });

  it('should GET /v1/api-keys when listing api keys', () => {
    const response = [
      { id: 'k1', name: 'default', createdAt: '2024-01-01T00:00:00Z', isActive: true, revokedAt: null }
    ];

    service.listApiKeys().subscribe((res) => expect(res).toEqual(response));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/api-keys`);
    expect(req.request.method).toBe('GET');
    req.flush(response);
  });

  it('should POST to /v1/api-keys when creating an additional api key', () => {
    const response = { id: 'k2', name: 'secondary', plaintextKey: 'pk_efgh5678' };

    service.createApiKey('secondary').subscribe((res) => expect(res).toEqual(response));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/api-keys`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ name: 'secondary' });
    req.flush(response);
  });

  it('should DELETE /v1/api-keys/{id} when revoking an api key', () => {
    service.revokeApiKey('k1').subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/api-keys/k1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });
  });
});
