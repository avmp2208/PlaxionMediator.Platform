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
  requestType: string | null;
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

export interface RequestTypeRankingItem {
  requestType: string;
  executionCount: number;
  p95: number;
  p99: number;
  failurePercent: number;
}

export interface PagedRequestTypeRankingResponse {
  items: RequestTypeRankingItem[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
}

export interface TenantRegistryEntry {
  tenantId: string;
  tenantName: string;
  apiKeyId: string;
  apiKeyName: string;
  maskedKey: string;
  createdAt: string;
}

/**
 * Server-side tenant membership role from GET /v1/tenants.
 * This is TenantMembership.role (Owner/Member), NOT an OIDC/IdP role.
 */
export type TenantMembershipRoleName = 'Owner' | 'Member' | string;

/** Server-side tenant membership row from GET /v1/tenants (OIDC mode). */
export interface TenantListItem {
  tenantId: string;
  /** TenantMembership role (Owner/Member). Do not confuse with OIDC/IdP roles. */
  role: TenantMembershipRoleName;
  createdAt: string;
  /** Optional display name enriched from local create response / registry. */
  tenantName?: string;
}

/** Invitation status values returned by Platform.Api (enum ToString). */
export type InvitationStatus = 'Pending' | 'Claimed' | 'Revoked' | string;

/** GET/POST /v1/invitations response item. */
export interface InvitationItem {
  id: string;
  tenantId: string;
  email: string;
  status: InvitationStatus;
  createdAt: string;
  claimedAt: string | null;
}

export interface CreateInvitationsRequest {
  email: string;
  tenantIds: string[];
}

export interface CreateInvitationsResponse {
  invitations: InvitationItem[];
  skippedTenantIds: string[];
}
