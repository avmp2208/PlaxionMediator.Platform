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
