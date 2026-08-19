using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Platform.Hosting.Configuration;

/// <summary>
/// Top-level platform identity / public surface configuration.
/// Bound from the "Platform" configuration section.
/// </summary>
public sealed class PlatformOptions
{
    public const string SectionName = "Platform";

    /// <summary>Logical deployment environment label (may mirror ASPNETCORE_ENVIRONMENT).</summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>Public base URL of this service (used for diagnostics / future link generation).</summary>
    [Url]
    public string? PublicBaseUrl { get; set; }
}

/// <summary>
/// Storage provider selection. Bound from the "Storage" configuration section.
/// Supported values: InMemory, Sqlite, MySql, PostgreSql (alias: Postgres).
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Storage engine name. Case-insensitive.</summary>
    [Required]
    public string Provider { get; set; } = "InMemory";
}

/// <summary>
/// Ingestion payload guardrails. Bound from the "Ingestion" configuration section.
/// </summary>
public sealed class IngestionOptions
{
    public const string SectionName = "Ingestion";

    /// <summary>Maximum HTTP request body size in bytes (Kestrel + MVC request size limit).</summary>
    [Range(1, long.MaxValue)]
    public long MaxRequestSizeBytes { get; set; } = 1_048_576;

    /// <summary>Maximum metric samples accepted in a single JSON ingest request.</summary>
    [Range(1, int.MaxValue)]
    public int MaxSamplesPerRequest { get; set; } = 5_000;

    /// <summary>Maximum distinct metric names accepted in a single JSON ingest request.</summary>
    [Range(1, int.MaxValue)]
    public int MaxMetricNamesPerRequest { get; set; } = 200;
}

/// <summary>
/// CORS allowed origins. Bound from the "Cors" configuration section.
/// </summary>
public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    /// <summary>Browser origins permitted to call Platform.Api. Empty means no origins (except Development defaults).</summary>
    public string[] AllowedOrigins { get; set; } = [];
}

/// <summary>
/// Security-related settings that may be supplied via configuration / env vars.
/// Bound from the "Security" configuration section. No secrets are required at
/// startup for MVP API-key auth (keys live in storage); this section is reserved
/// for optional bootstrap overrides and future hardening knobs.
/// </summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    // Reserved for future hardening knobs (e.g. signing/rotation settings).
    // The old deterministic dev-key bootstrap has been removed: every tenant
    // (including local development) now provisions its own API key via the
    // self-service POST /v1/tenants endpoint.
}

/// <summary>
/// Observability settings. Bound from the "Observability" configuration section.
/// OTLP exporter credentials, when used, must come from env vars — never commit secrets.
/// </summary>
public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    /// <summary>Optional OTLP endpoint for platform self-telemetry (not customer ingest).</summary>
    public string? OtlpEndpoint { get; set; }

    /// <summary>Optional OTLP headers (e.g. authorization). Prefer env var Observability__OtlpHeaders.</summary>
    public string? OtlpHeaders { get; set; }
}

/// <summary>
/// Database schema migration behavior. Bound from the "Database" configuration section.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>
    /// When true, apply EF Core migrations at startup (MigrateAsync).
    /// When null (default), auto-migrate only in Development and Testing.
    /// Production/Staging never auto-migrate unless this is explicitly true
    /// (prefer the --migrate CLI switch for one-shot migration jobs).
    /// </summary>
    public bool? AutoMigrate { get; set; }

    /// <summary>
    /// When true in Production/Staging, fail startup if pending migrations exist.
    /// Defaults to true. Set false only for break-glass scenarios.
    /// </summary>
    public bool FailOnPendingMigrations { get; set; } = true;
}

public sealed class AggregationOptionsValidator : IValidateOptions<Aggregation.AggregationOptions>
{
    public ValidateOptionsResult Validate(string? name, Aggregation.AggregationOptions options)
    {
        if (options.Interval <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail("Aggregation:Interval must be a positive TimeSpan.");
        }

        if (options.BucketSize <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail("Aggregation:BucketSize must be a positive TimeSpan.");
        }

        return ValidateOptionsResult.Success;
    }
}

public sealed class IngestionOptionsValidator : IValidateOptions<IngestionOptions>
{
    public ValidateOptionsResult Validate(string? name, IngestionOptions options)
    {
        if (options.MaxRequestSizeBytes <= 0)
        {
            return ValidateOptionsResult.Fail("Ingestion:MaxRequestSizeBytes must be positive.");
        }

        if (options.MaxSamplesPerRequest <= 0)
        {
            return ValidateOptionsResult.Fail("Ingestion:MaxSamplesPerRequest must be positive.");
        }

        if (options.MaxMetricNamesPerRequest <= 0)
        {
            return ValidateOptionsResult.Fail("Ingestion:MaxMetricNamesPerRequest must be positive.");
        }

        return ValidateOptionsResult.Success;
    }
}

public sealed class StorageOptionsValidator : IValidateOptions<StorageOptions>
{
    internal static readonly HashSet<string> SupportedProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "InMemory",
        "Sqlite",
        "MySql",
        "PostgreSql",
        "Postgres"
    };

    public ValidateOptionsResult Validate(string? name, StorageOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Provider))
        {
            return ValidateOptionsResult.Fail("Storage:Provider is required.");
        }

        if (!SupportedProviders.Contains(options.Provider.Trim()))
        {
            return ValidateOptionsResult.Fail(
                $"Storage:Provider '{options.Provider}' is not supported. " +
                "Supported values: InMemory, Sqlite, MySql, PostgreSql (alias: Postgres).");
        }

        return ValidateOptionsResult.Success;
    }
}
