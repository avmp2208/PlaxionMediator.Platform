namespace Platform.Storage;

/// <summary>
/// Provider-neutral persistence for email-based tenant invitations.
/// Authorization (Owner-only create/list/revoke) lives outside storage.
/// </summary>
public interface ITenantInvitationStore
{
    Task AddAsync(TenantInvitation invitation, CancellationToken cancellationToken = default);

    Task<TenantInvitation?> GetByIdAsync(Guid invitationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the active Pending invitation for (tenantId, normalizedEmail), if any.
    /// </summary>
    Task<TenantInvitation?> GetPendingAsync(
        Guid tenantId,
        string normalizedEmail,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TenantInvitation>> ListByTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists Pending invitations whose normalized email matches (ordinal ignore-case
    /// comparison is the caller's responsibility when normalizing input).
    /// </summary>
    Task<IReadOnlyList<TenantInvitation>> ListPendingByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists status/claim fields for an existing invitation row.
    /// </summary>
    Task UpdateAsync(TenantInvitation invitation, CancellationToken cancellationToken = default);
}
