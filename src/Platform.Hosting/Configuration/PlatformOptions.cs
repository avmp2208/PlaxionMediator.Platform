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
/// OIDC / JWT Bearer authentication settings for human-facing Platform.Api
/// endpoints. Bound from the "Authentication" configuration section.
/// Machine/telemetry auth continues to use API keys (separate scheme).
/// </summary>
public sealed class AuthenticationOptions
{
    public const string SectionName = "Authentication";

    /// <summary>
    /// When true, register and validate JWT Bearer tokens against
    /// <see cref="Authority"/> / <see cref="Audience"/> via OIDC metadata discovery.
    /// Defaults to false so local Development can run without an IdP.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>OIDC authority (issuer) base URL, e.g. https://login.example.com/.</summary>
    public string? Authority { get; set; }

    /// <summary>Expected JWT audience (API resource identifier).</summary>
    public string? Audience { get; set; }

    /// <summary>
    /// When true, OIDC metadata and JWKS must be fetched over HTTPS.
    /// Defaults to true; set false only for local IdP testing.
    /// </summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    /// Explicit Production opt-out: when <see cref="Enabled"/> is false in
    /// Production, startup fails unless this is set to true. Development may
    /// leave authentication disabled without this flag.
    /// </summary>
    public bool AllowAnonymousProduction { get; set; }
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

/// <summary>
/// Validates OIDC authentication configuration fields. The Production
/// AllowAnonymousProduction fail-fast is enforced only in Platform.Api
/// (see Program.cs) so machine hosts like Platform.Ingestion are unaffected.
/// </summary>
public sealed class AuthenticationOptionsValidator : IValidateOptions<AuthenticationOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthenticationOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        if (string.IsNullOrWhiteSpace(options.Authority))
        {
            return ValidateOptionsResult.Fail(
                "Authentication:Authority is required when Authentication:Enabled is true.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            return ValidateOptionsResult.Fail(
                "Authentication:Audience is required when Authentication:Enabled is true.");
        }

        return ValidateOptionsResult.Success;
    }
}

/// <summary>
/// Public, non-secret Dashboard runtime settings. Bound from the "Dashboard"
/// configuration section. Surfaced via GET /v1/platform/config and the
/// container-generated wwwroot/assets/config.json (no client secrets).
/// </summary>
public sealed class DashboardOptions
{
    public const string SectionName = "Dashboard";

    /// <summary>
    /// API base URL the SPA should call. Empty string means same-origin.
    /// Mirrors the container API_BASE_URL entrypoint variable.
    /// </summary>
    public string? ApiBaseUrl { get; set; }

    /// <summary>Public OIDC SPA client settings (no client secret).</summary>
    public DashboardOidcOptions Oidc { get; set; } = new();
}

/// <summary>
/// Browser OIDC client settings for the Angular Dashboard.
/// Bound from "Dashboard:Oidc". Public-client only — never a client secret.
/// </summary>
public sealed class DashboardOidcOptions
{
    public const string SectionName = "Dashboard:Oidc";

    /// <summary>Public OIDC client_id registered at the identity provider.</summary>
    public string? ClientId { get; set; }

    /// <summary>OIDC scopes requested by the SPA (Authorization Code + PKCE).</summary>
    public string[] Scopes { get; set; } = ["openid", "profile", "email"];

    /// <summary>In-app path for the OIDC redirect callback (e.g. /auth/callback).</summary>
    public string RedirectPath { get; set; } = "/auth/callback";

    /// <summary>In-app path after end-session (e.g. /).</summary>
    public string PostLogoutRedirectPath { get; set; } = "/";
}

/// <summary>
/// How tenant-creation authorization decides who may call POST /v1/tenants
/// when Authentication:Enabled is true.
/// </summary>
public enum TenantCreationMode
{
    /// <summary>Any principal authenticated via JWT Bearer may create a tenant.</summary>
    AnyAuthenticatedUser = 0,

    /// <summary>Principal must have <see cref="TenantCreationOptions.RequiredRole"/>.</summary>
    Role = 1,

    /// <summary>
    /// Principal must carry <see cref="TenantCreationOptions.RequiredClaimType"/>
    /// with one of <see cref="TenantCreationOptions.RequiredClaimValues"/> (or any
    /// value when the values list is empty).
    /// </summary>
    Claim = 2
}

/// <summary>
/// Authorization rules for self-service tenant bootstrap.
/// Bound from the "Authorization:TenantCreation" configuration section.
/// Claim/role checks are provider-neutral (configured claim type/values only).
/// </summary>
public sealed class TenantCreationOptions
{
    public const string SectionName = "Authorization:TenantCreation";

    /// <summary>Authorization mode. Defaults to any authenticated JWT user.</summary>
    public TenantCreationMode Mode { get; set; } = TenantCreationMode.AnyAuthenticatedUser;

    /// <summary>Required role name when <see cref="Mode"/> is <see cref="TenantCreationMode.Role"/>.</summary>
    public string? RequiredRole { get; set; }

    /// <summary>Claim type to require when <see cref="Mode"/> is <see cref="TenantCreationMode.Claim"/>.</summary>
    public string? RequiredClaimType { get; set; }

    /// <summary>
    /// Allowed values for <see cref="RequiredClaimType"/>. When empty in Claim
    /// mode, presence of the claim type alone is sufficient.
    /// </summary>
    public string[] RequiredClaimValues { get; set; } = [];
}

/// <summary>Validates <see cref="TenantCreationOptions"/> field combinations per mode.</summary>
public sealed class TenantCreationOptionsValidator : IValidateOptions<TenantCreationOptions>
{
    public ValidateOptionsResult Validate(string? name, TenantCreationOptions options)
    {
        switch (options.Mode)
        {
            case TenantCreationMode.AnyAuthenticatedUser:
                return ValidateOptionsResult.Success;

            case TenantCreationMode.Role:
                if (string.IsNullOrWhiteSpace(options.RequiredRole))
                {
                    return ValidateOptionsResult.Fail(
                        "Authorization:TenantCreation:RequiredRole is required when Mode is Role.");
                }

                return ValidateOptionsResult.Success;

            case TenantCreationMode.Claim:
                if (string.IsNullOrWhiteSpace(options.RequiredClaimType))
                {
                    return ValidateOptionsResult.Fail(
                        "Authorization:TenantCreation:RequiredClaimType is required when Mode is Claim.");
                }

                return ValidateOptionsResult.Success;

            default:
                return ValidateOptionsResult.Fail(
                    $"Authorization:TenantCreation:Mode '{options.Mode}' is not supported.");
        }
    }
}

/// <summary>
/// How platform-administrator authorization is evaluated for legacy-tenant
/// reconciliation and cross-tenant operational bypass.
/// </summary>
public enum PlatformAdministratorMode
{
    /// <summary>
    /// No principal is treated as a platform administrator. Safe default for
    /// new and upgraded deployments until operators deliberately enable it.
    /// </summary>
    Disabled = 0,

    /// <summary>Principal must have <see cref="PlatformAdministratorOptions.RequiredRole"/>.</summary>
    Role = 1,

    /// <summary>
    /// Principal must carry <see cref="PlatformAdministratorOptions.RequiredClaimType"/>
    /// with one of <see cref="PlatformAdministratorOptions.RequiredClaimValues"/> (or any
    /// value when the values list is empty).
    /// </summary>
    Claim = 2
}

/// <summary>
/// Authorization rules for platform operators who may administer any tenant
/// (including pre-OIDC legacy tenants with zero membership rows) and call the
/// explicit membership reconciliation endpoint.
/// Bound from the "Authorization:PlatformAdministrator" configuration section.
/// Keep Mode=Disabled until an upgrade run needs bootstrap access; then set
/// Role/Claim against a tightly scoped IdP group and revoke after reconciliation.
/// </summary>
public sealed class PlatformAdministratorOptions
{
    public const string SectionName = "Authorization:PlatformAdministrator";

    /// <summary>Authorization mode. Defaults to Disabled (no platform admins).</summary>
    public PlatformAdministratorMode Mode { get; set; } = PlatformAdministratorMode.Disabled;

    /// <summary>Required role name when <see cref="Mode"/> is <see cref="PlatformAdministratorMode.Role"/>.</summary>
    public string? RequiredRole { get; set; }

    /// <summary>Claim type to require when <see cref="Mode"/> is <see cref="PlatformAdministratorMode.Claim"/>.</summary>
    public string? RequiredClaimType { get; set; }

    /// <summary>
    /// Allowed values for <see cref="RequiredClaimType"/>. When empty in Claim
    /// mode, presence of the claim type alone is sufficient.
    /// </summary>
    public string[] RequiredClaimValues { get; set; } = [];
}

/// <summary>Validates <see cref="PlatformAdministratorOptions"/> field combinations per mode.</summary>
public sealed class PlatformAdministratorOptionsValidator : IValidateOptions<PlatformAdministratorOptions>
{
    public ValidateOptionsResult Validate(string? name, PlatformAdministratorOptions options)
    {
        switch (options.Mode)
        {
            case PlatformAdministratorMode.Disabled:
                return ValidateOptionsResult.Success;

            case PlatformAdministratorMode.Role:
                if (string.IsNullOrWhiteSpace(options.RequiredRole))
                {
                    return ValidateOptionsResult.Fail(
                        "Authorization:PlatformAdministrator:RequiredRole is required when Mode is Role.");
                }

                return ValidateOptionsResult.Success;

            case PlatformAdministratorMode.Claim:
                if (string.IsNullOrWhiteSpace(options.RequiredClaimType))
                {
                    return ValidateOptionsResult.Fail(
                        "Authorization:PlatformAdministrator:RequiredClaimType is required when Mode is Claim.");
                }

                return ValidateOptionsResult.Success;

            default:
                return ValidateOptionsResult.Fail(
                    $"Authorization:PlatformAdministrator:Mode '{options.Mode}' is not supported.");
        }
    }
}
