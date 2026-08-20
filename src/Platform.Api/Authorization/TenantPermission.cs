namespace Platform.Api.Authorization;

/// <summary>
/// Platform-internal tenant permissions derived from
/// <see cref="Platform.Storage.TenantMembershipRole"/>. Intentionally small and
/// extensible — add new values (e.g. ManageSlo, ManageAlerts) without building
/// a full RBAC framework. Mapping lives in <see cref="ITenantAuthorizationService"/>.
/// </summary>
public enum TenantPermission
{
    /// <summary>Read tenant-scoped resources (Owner and Member).</summary>
    ViewTenant = 0,

    /// <summary>Create / list / revoke invitations (Owner only).</summary>
    ManageInvitations = 1,

    /// <summary>Create / list / revoke telemetry API keys (Owner only).</summary>
    ManageApiKeys = 2
}
