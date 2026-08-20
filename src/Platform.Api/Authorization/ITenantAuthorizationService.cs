using System.Security.Claims;
using Platform.Storage;

namespace Platform.Api.Authorization;

/// <summary>
/// Centralized in-tenant authorization against <see cref="TenantMembership.Role"/>.
/// Keeps Owner/Member checks out of controllers so future permissions can be
/// added in one place. Separate from OIDC/IdP roles (TenantCreation policy).
/// </summary>
public interface ITenantAuthorizationService
{
    /// <summary>
    /// Returns the caller's membership for <paramref name="tenantId"/>, or null
    /// when the principal has no iss/sub or no membership row.
    /// </summary>
    Task<TenantMembership?> GetMembershipAsync(
        ClaimsPrincipal user,
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the principal's membership role grants <paramref name="permission"/>.
    /// Platform administrators are not auto-granted Owner permissions here —
    /// break-glass remains on the TenantMember policy path only.
    /// </summary>
    Task<bool> HasPermissionAsync(
        ClaimsPrincipal user,
        Guid tenantId,
        TenantPermission permission,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the principal is an Owner of <paramref name="tenantId"/>.
    /// </summary>
    Task<bool> IsOwnerAsync(
        ClaimsPrincipal user,
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
