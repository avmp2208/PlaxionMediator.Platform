using System.Collections.Concurrent;

namespace Platform.Storage;

/// <summary>
/// In-memory tenant membership store for tests and Development.
/// </summary>
public sealed class InMemoryTenantMembershipStore : ITenantMembershipStore
{
    private readonly ConcurrentDictionary<Guid, TenantMembership> _memberships = new();

    public Task AddAsync(TenantMembership membership, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _memberships[membership.Id] = membership;
        return Task.CompletedTask;
    }

    public Task<TenantMembership?> GetByIdAsync(Guid membershipId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _memberships.TryGetValue(membershipId, out var membership);
        return Task.FromResult(membership);
    }

    public Task<TenantMembership?> GetAsync(
        Guid tenantId,
        string identityIssuer,
        string identitySubject,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var membership = _memberships.Values.FirstOrDefault(m =>
            m.TenantId == tenantId
            && string.Equals(m.IdentityIssuer, identityIssuer, StringComparison.Ordinal)
            && string.Equals(m.IdentitySubject, identitySubject, StringComparison.Ordinal));
        return Task.FromResult(membership);
    }

    public Task<IReadOnlyList<TenantMembership>> ListByTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<TenantMembership> result = _memberships.Values
            .Where(m => m.TenantId == tenantId)
            .OrderBy(m => m.CreatedAt)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<TenantMembership>> ListByIdentityAsync(
        string identityIssuer,
        string identitySubject,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<TenantMembership> result = _memberships.Values
            .Where(m =>
                string.Equals(m.IdentityIssuer, identityIssuer, StringComparison.Ordinal)
                && string.Equals(m.IdentitySubject, identitySubject, StringComparison.Ordinal))
            .OrderBy(m => m.CreatedAt)
            .ToList();
        return Task.FromResult(result);
    }
}
