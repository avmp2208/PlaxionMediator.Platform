namespace Platform.Identity;

/// <summary>
/// Result of issuing a new API key. PlaintextKey is only ever available
/// here, at issuance time - it is never persisted or logged.
/// </summary>
public sealed record ApiKeyCreationResult(Guid ApiKeyId, string PlaintextKey);

/// <summary>
/// Safe metadata view of an API key (never includes plaintext or hash).
/// </summary>
public sealed record ApiKeyInfo(Guid Id, Guid TenantId, string Name, DateTimeOffset CreatedAt, bool IsActive, DateTimeOffset? RevokedAt);
