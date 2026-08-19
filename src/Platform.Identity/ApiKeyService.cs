using Platform.Storage;

namespace Platform.Identity;

/// <summary>
/// Organization-scoped API key issuance and validation. Produces a trusted
/// TenantId only from a valid, non-revoked key - never from client input.
/// </summary>
public sealed class ApiKeyService
{
    private readonly IApiKeyStore _store;

    public ApiKeyService(IApiKeyStore store)
    {
        _store = store;
    }

    public Task<ApiKeyCreationResult> CreateAsync(Guid tenantId, string name, CancellationToken cancellationToken = default) =>
        CreateWithPlaintextAsync(tenantId, name, ApiKeyHasher.GenerateKey(), cancellationToken);

    /// <summary>
    /// Issues an API key using a caller-supplied plaintext value instead of a
    /// freshly generated random one. Used for the local-dev deterministic
    /// seed key so it survives process restarts of an in-memory store
    /// without requiring the developer to re-copy a new key every time.
    /// Never use this for real tenant keys.
    /// </summary>
    public async Task<ApiKeyCreationResult> CreateWithPlaintextAsync(
        Guid tenantId,
        string name,
        string plaintext,
        CancellationToken cancellationToken = default)
    {
        var apiKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            HashedKey = ApiKeyHasher.Hash(plaintext),
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _store.AddAsync(apiKey, cancellationToken);
        return new ApiKeyCreationResult(apiKey.Id, plaintext);
    }

    public Task RevokeAsync(Guid apiKeyId, CancellationToken cancellationToken = default) =>
        _store.RevokeAsync(apiKeyId, cancellationToken);

    /// <summary>
    /// Lists API key metadata for a tenant. Never returns plaintext or hashes.
    /// </summary>
    public async Task<IReadOnlyList<ApiKeyInfo>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var keys = await _store.ListByTenantAsync(tenantId, cancellationToken);
        return keys
            .Select(k => new ApiKeyInfo(k.Id, k.TenantId, k.Name, k.CreatedAt, k.IsActive, k.RevokedAt))
            .ToList();
    }

    /// <summary>
    /// Validates a plaintext API key. Returns the trusted TenantId when the
    /// key is active and matches; otherwise null.
    /// </summary>
    public async Task<Guid?> ValidateAsync(string plaintextKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plaintextKey))
        {
            return null;
        }

        var activeKeys = await _store.GetAllActiveAsync(cancellationToken);
        foreach (var key in activeKeys)
        {
            if (ApiKeyHasher.Matches(plaintextKey, key.HashedKey))
            {
                return key.TenantId;
            }
        }

        return null;
    }
}
