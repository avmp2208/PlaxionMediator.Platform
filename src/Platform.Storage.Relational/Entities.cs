namespace Platform.Storage.Relational;

// SQLite/EF has limited DateTimeOffset translation support, so timestamps
// are stored as UTC DateTime and converted at the store boundary.

internal sealed class MetricSampleEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string MetricName { get; set; } = string.Empty;
    public string? RequestType { get; set; }
    public double Value { get; set; }
    public DateTime TimestampUtc { get; set; }
}

internal sealed class AggregateRollupEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ServiceId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string MetricName { get; set; } = string.Empty;
    public string? RequestType { get; set; }
    public DateTime BucketStartUtc { get; set; }
    public DateTime BucketEndUtc { get; set; }
    public int Count { get; set; }
    public double Min { get; set; }
    public double Max { get; set; }
    public double Avg { get; set; }
    public double P50 { get; set; }
    public double P95 { get; set; }
    public double P99 { get; set; }
}

internal sealed class ServiceEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
}

internal sealed class ApiKeyEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string HashedKey { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}

internal static class TimeMapping
{
    // PostgreSQL's "timestamp" types only retain microsecond precision, while
    // .NET DateTime/DateTimeOffset carry 100ns ticks. Without truncating here,
    // a value computed in memory (full tick precision) would no longer equal
    // the same value once round-tripped through Postgres, breaking equality
    // comparisons/upserts that rely on matching timestamps (e.g. aggregate
    // rollup bucket lookups). Truncate consistently so both sides agree.
    private const long TicksPerMicrosecond = 10;

    public static DateTime ToUtcDateTime(DateTimeOffset value)
    {
        var utc = value.UtcDateTime;
        var truncatedTicks = utc.Ticks - (utc.Ticks % TicksPerMicrosecond);
        return new DateTime(truncatedTicks, DateTimeKind.Utc);
    }

    public static DateTimeOffset ToOffset(DateTime utc) =>
        new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
}
