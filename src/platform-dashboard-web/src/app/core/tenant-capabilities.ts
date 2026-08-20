/**
 * Dashboard capabilities derived from server-provided TenantMembership.role
 * (GET /v1/tenants). Do NOT inspect OIDC/IdP roles or claims here — those are
 * used only for tenant creation (Authorization:TenantCreation) on the backend.
 *
 * Structured so future granular permissions (ManageMembers, ManageSlo,
 * ManageAlerts, …) can be added without rewriting call sites.
 */

export type TenantCapability =
  | 'ViewTenant'
  | 'ManageInvitations'
  | 'ManageApiKeys';

const OWNER_CAPABILITIES: ReadonlySet<TenantCapability> = new Set([
  'ViewTenant',
  'ManageInvitations',
  'ManageApiKeys'
]);

const MEMBER_CAPABILITIES: ReadonlySet<TenantCapability> = new Set(['ViewTenant']);

/** Normalize server role string for comparison. */
export function normalizeMembershipRole(role: string | null | undefined): string {
  return (role ?? '').trim().toLowerCase();
}

/**
 * Map a server membership role to the set of dashboard capabilities.
 * Unknown roles receive view-only access (safe default).
 */
export function capabilitiesForMembershipRole(
  role: string | null | undefined
): ReadonlySet<TenantCapability> {
  const normalized = normalizeMembershipRole(role);
  if (normalized === 'owner') {
    return OWNER_CAPABILITIES;
  }
  return MEMBER_CAPABILITIES;
}

export function hasCapability(
  role: string | null | undefined,
  capability: TenantCapability
): boolean {
  return capabilitiesForMembershipRole(role).has(capability);
}

/** True when the membership role grants invitation management (Owner). */
export function canManageInvitations(role: string | null | undefined): boolean {
  return hasCapability(role, 'ManageInvitations');
}

/** True when the membership role grants API key management (Owner). */
export function canManageApiKeys(role: string | null | undefined): boolean {
  return hasCapability(role, 'ManageApiKeys');
}

/**
 * Aggregate capabilities across many tenant memberships (e.g. show global
 * Invitations nav if the user is Owner of at least one tenant).
 */
export function anyMembershipHasCapability(
  memberships: ReadonlyArray<{ role: string }>,
  capability: TenantCapability
): boolean {
  return memberships.some((m) => hasCapability(m.role, capability));
}
