using System.Collections.Concurrent;

namespace Platform.Storage;

/// <summary>
/// In-memory tenant invitation store for tests and Development.
/// Enforces a single Pending invitation per (TenantId, NormalizedEmail).
/// </summary>
public sealed class InMemoryTenantInvitationStore : ITenantInvitationStore
{
    private readonly ConcurrentDictionary<Guid, TenantInvitation> _invitations = new();
    private readonly object _gate = new();

    public Task AddAsync(TenantInvitation invitation, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (invitation.Status == TenantInvitationStatus.Pending
                && _invitations.Values.Any(i =>
                    i.Status == TenantInvitationStatus.Pending
                    && i.TenantId == invitation.TenantId
                    && string.Equals(i.NormalizedEmail, invitation.NormalizedEmail, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    "A pending invitation already exists for this tenant and email.");
            }

            if (!_invitations.TryAdd(invitation.Id, invitation))
            {
                throw new InvalidOperationException($"Invitation '{invitation.Id}' already exists.");
            }
        }

        return Task.CompletedTask;
    }

    public Task<TenantInvitation?> GetByIdAsync(Guid invitationId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _invitations.TryGetValue(invitationId, out var invitation);
        return Task.FromResult(invitation);
    }

    public Task<TenantInvitation?> GetPendingAsync(
        Guid tenantId,
        string normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var invitation = _invitations.Values.FirstOrDefault(i =>
            i.Status == TenantInvitationStatus.Pending
            && i.TenantId == tenantId
            && string.Equals(i.NormalizedEmail, normalizedEmail, StringComparison.Ordinal));
        return Task.FromResult(invitation);
    }

    public Task<IReadOnlyList<TenantInvitation>> ListByTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<TenantInvitation> result = _invitations.Values
            .Where(i => i.TenantId == tenantId)
            .OrderByDescending(i => i.CreatedAt)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<TenantInvitation>> ListPendingByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<TenantInvitation> result = _invitations.Values
            .Where(i =>
                i.Status == TenantInvitationStatus.Pending
                && string.Equals(i.NormalizedEmail, normalizedEmail, StringComparison.Ordinal))
            .OrderBy(i => i.CreatedAt)
            .ToList();
        return Task.FromResult(result);
    }

    public Task UpdateAsync(TenantInvitation invitation, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_invitations.ContainsKey(invitation.Id))
            {
                throw new InvalidOperationException($"Invitation '{invitation.Id}' was not found.");
            }

            _invitations[invitation.Id] = invitation;
        }

        return Task.CompletedTask;
    }
}
