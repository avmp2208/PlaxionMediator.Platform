export interface ServiceSummary {
  id: string;
  name: string;
  environment: string;
}

export type TimeRange = '1h' | '24h' | '7d';

export interface MetricPoint {
  bucketStart: string;
  bucketEnd: string;
  metricName: string;
  count: number;
  min: number;
  max: number;
  avg: number;
  p50: number;
  p95: number;
  p99: number;
}

export interface MetricSeries {
  metricName: string;
  unit: string;
  points: MetricPoint[];
}

export interface CreateTenantResponse {
  tenantId: string;
  tenantName: string;
  apiKeyId: string;
  apiKeyName: string;
  plaintextKey: string;
  createdAt: string;
}

export interface ApiKeyListItem {
  id: string;
  name: string;
  createdAt: string;
  isActive: boolean;
  revokedAt: string | null;
}

export interface CreateApiKeyResponse {
  id: string;
  name: string;
  plaintextKey: string;
}

export interface TenantRegistryEntry {
  tenantId: string;
  tenantName: string;
  apiKeyId: string;
  apiKeyName: string;
  maskedKey: string;
  createdAt: string;
}
