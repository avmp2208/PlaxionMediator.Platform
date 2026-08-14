using System.Collections.Concurrent;

namespace Platform.Storage;

/// <summary>
/// In-memory API key store for tests and Development. TenantId scoping and
/// hashing invariants are enforced by Platform.Identity.ApiKeyService.
/// </summary>
public sealed class InMemoryApiKeyStore : IApiKeyStore
{
    private readonly ConcurrentDictionary<Guid, ApiKey> _keys = new();

    public Task AddAsync(ApiKey apiKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _keys[apiKey.Id] = apiKey;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ApiKey>> GetAllActiveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ApiKey> result = _keys.Values.Where(k => k.IsActive).ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<ApiKey>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ApiKey> result = _keys.Values
            .Where(k => k.TenantId == tenantId)
            .OrderByDescending(k => k.CreatedAt)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<ApiKey?> GetByIdAsync(Guid apiKeyId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _keys.TryGetValue(apiKeyId, out var key);
        return Task.FromResult(key);
    }

    public Task RevokeAsync(Guid apiKeyId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_keys.TryGetValue(apiKeyId, out var key))
        {
            key.RevokedAt = DateTimeOffset.UtcNow;
        }

        return Task.CompletedTask;
    }
}
