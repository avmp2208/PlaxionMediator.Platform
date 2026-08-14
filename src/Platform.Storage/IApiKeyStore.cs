namespace Platform.Storage;

/// <summary>
/// Provider-neutral persistence contract for API keys. Hashing and
/// tenant-scoping invariants are enforced by Platform.Identity services.
/// </summary>
public interface IApiKeyStore
{
    Task AddAsync(ApiKey apiKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApiKey>> GetAllActiveAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApiKey>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<ApiKey?> GetByIdAsync(Guid apiKeyId, CancellationToken cancellationToken = default);
    Task RevokeAsync(Guid apiKeyId, CancellationToken cancellationToken = default);
}
