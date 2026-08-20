namespace Platform.Storage;

/// <summary>
/// Provider-neutral persistence contract for OIDC subject ↔ tenant membership.
/// Authorization policies that interpret membership live outside storage.
/// </summary>
public interface ITenantMembershipStore
{
    Task AddAsync(TenantMembership membership, CancellationToken cancellationToken = default);

    Task<TenantMembership?> GetByIdAsync(Guid membershipId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up a single membership by tenant + identity issuer/subject.
    /// Returns null when the identity is not a member of the tenant.
    /// </summary>
    Task<TenantMembership?> GetAsync(
        Guid tenantId,
        string identityIssuer,
        string identitySubject,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TenantMembership>> ListByTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all tenant memberships for a given OIDC identity (issuer + subject).
    /// </summary>
    Task<IReadOnlyList<TenantMembership>> ListByIdentityAsync(
        string identityIssuer,
        string identitySubject,
        CancellationToken cancellationToken = default);
}
