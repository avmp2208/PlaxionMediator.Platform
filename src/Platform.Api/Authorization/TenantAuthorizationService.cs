using System.Security.Claims;
using Platform.Storage;

namespace Platform.Api.Authorization;

/// <summary>
/// Minimal role→permission mapper. Owner receives every permission; Member
/// receives <see cref="TenantPermission.ViewTenant"/> only. Extend
/// <see cref="RoleGrants"/> when new permissions are introduced.
/// </summary>
public sealed class TenantAuthorizationService : ITenantAuthorizationService
{
    private readonly ITenantMembershipStore _membershipStore;

    public TenantAuthorizationService(ITenantMembershipStore membershipStore)
    {
        _membershipStore = membershipStore;
    }

    public async Task<TenantMembership?> GetMembershipAsync(
        ClaimsPrincipal user,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        if (!OidcIdentityClaims.TryGetIssuerAndSubject(user, out var issuer, out var subject))
        {
            return null;
        }

        return await _membershipStore.GetAsync(tenantId, issuer, subject, cancellationToken);
    }

    public async Task<bool> HasPermissionAsync(
        ClaimsPrincipal user,
        Guid tenantId,
        TenantPermission permission,
        CancellationToken cancellationToken = default)
    {
        var membership = await GetMembershipAsync(user, tenantId, cancellationToken);
        return membership is not null && RoleGrants(membership.Role, permission);
    }

    public Task<bool> IsOwnerAsync(
        ClaimsPrincipal user,
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        HasPermissionAsync(user, tenantId, TenantPermission.ManageInvitations, cancellationToken);

    /// <summary>
    /// Single mapping table for internal roles. Add rows/columns here rather
    /// than scattering role comparisons across controllers.
    /// </summary>
    internal static bool RoleGrants(TenantMembershipRole role, TenantPermission permission) =>
        role switch
        {
            TenantMembershipRole.Owner => true,
            TenantMembershipRole.Member => permission is TenantPermission.ViewTenant,
            _ => false
        };
}
