namespace Platform.Api;

/// <summary>
/// Named authorization policies for Platform.Api. JWT Bearer is intended for
/// human-facing tenant management; API key remains the default scheme for
/// existing machine-oriented endpoints until a later phase switches them.
/// </summary>
public static class PlatformAuthenticationPolicies
{
    /// <summary>
    /// Requires an authenticated principal from the JWT Bearer scheme
    /// (OIDC access token validated via Authority metadata discovery).
    /// </summary>
    public const string JwtBearer = "JwtBearer";

    /// <summary>
    /// Gates POST /v1/tenants when Authentication:Enabled=true. Built from
    /// <c>Authorization:TenantCreation</c> options (any authenticated user,
    /// role, or generic claim). When authentication is disabled the policy
    /// allows anonymous bootstrap for local/dev.
    /// </summary>
    public const string TenantCreation = "TenantCreation";

    /// <summary>
    /// Tenant-scoped resource access: API key whose trusted tenant_id matches
    /// the route tenantId, a JWT principal with a TenantMembership row for
    /// that tenant, or a configured platform administrator (legacy-tenant
    /// bypass). Failures are mapped to 404 (not 403) to avoid leaking
    /// cross-tenant identifiers.
    /// </summary>
    public const string TenantMember = "TenantMember";

    /// <summary>
    /// Platform operator policy for explicit membership reconciliation
    /// (POST /v1/tenants/{tenantId}/memberships). Built from
    /// <c>Authorization:PlatformAdministrator</c> (Disabled / Role / Claim).
    /// When authentication is disabled the policy denies all callers.
    /// </summary>
    public const string PlatformAdministrator = "PlatformAdministrator";
}
