namespace Platform.Storage;

/// <summary>
/// Deterministic dev tenant used only as a bootstrap default until real
/// tenant provisioning / API-key based auth (Platform.Identity) exists.
/// </summary>
public static class DevTenant
{
    public static readonly Guid Id = new Guid("00000000-0000-0000-0000-000000000001");
    public const string Name = "dev-tenant";
}

public sealed class Tenant
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

public sealed class Service
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
}

/// <summary>
/// Canonical persistence model for a single metric data point. This is
/// intentionally decoupled from the OTLP/OTel transport schema - mapping
/// happens in Platform.Ingestion.
/// </summary>
public sealed class MetricSample
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public string ServiceName { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
    public string MetricName { get; init; } = string.Empty;
    public double Value { get; init; }
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Optional request-type dimension (e.g. the CLR request type name),
    /// currently populated only for PlaxionMediator.OpenTelemetry metrics
    /// (plaxionmediator.request.count / .request.duration /
    /// .pipeline_exception.count). Null for metrics without this dimension.
    /// </summary>
    public string? RequestType { get; init; }
}

/// <summary>
/// A scheduled (non-streaming) rollup of MetricSample values over a fixed
/// time bucket, produced by Platform.Aggregation and consumed by
/// Platform.Api for range queries.
/// </summary>
public sealed class AggregateRollup
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public string ServiceName { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
    public string MetricName { get; init; } = string.Empty;

    /// <summary>
    /// Optional request-type dimension. Rollups are grouped per
    /// (TenantId, ServiceName, Environment, MetricName, RequestType,
    /// BucketStart). Null still rolls up together as before (backward
    /// compatible with metrics that don't carry this dimension).
    /// </summary>
    public string? RequestType { get; init; }

    public DateTimeOffset BucketStart { get; init; }
    public DateTimeOffset BucketEnd { get; init; }
    public int Count { get; init; }
    public double Min { get; init; }
    public double Max { get; init; }
    public double Avg { get; init; }
    public double P50 { get; init; }
    public double P95 { get; init; }
    public double P99 { get; init; }
}

/// <summary>
/// Persisted API key record. Only the hash is stored - the plaintext key is
/// shown to the caller exactly once, at creation time.
/// </summary>
public sealed class ApiKey
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string HashedKey { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsActive => RevokedAt is null;
}

/// <summary>
/// Role of an OIDC identity within a tenant. Stored as part of
/// <see cref="TenantMembership"/>; authorization policies interpret it later.
/// </summary>
public enum TenantMembershipRole
{
    Owner = 0,
    Member = 1
}

/// <summary>
/// Links an OIDC identity (issuer + subject) to a tenant with a role.
/// Provider-neutral model; EF/in-memory stores map to this shape.
/// </summary>
public sealed class TenantMembership
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }

    /// <summary>OIDC token issuer (iss claim).</summary>
    public string IdentityIssuer { get; init; } = string.Empty;

    /// <summary>OIDC subject identifier (sub claim).</summary>
    public string IdentitySubject { get; init; } = string.Empty;

    public TenantMembershipRole Role { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
