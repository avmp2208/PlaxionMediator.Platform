using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Platform.Aggregation;
using Platform.Api;
using Platform.Api.Authorization;
using Platform.Hosting;
using Platform.Hosting.Configuration;
using Platform.Identity;
using Platform.Storage.Relational;
using Platform.Storage.Relational.Health;
using CorsOptions = Platform.Hosting.Configuration.CorsOptions;
using AuthenticationOptions = Platform.Hosting.Configuration.AuthenticationOptions;

var migrateOnly = MigrateCommand.IsMigrateOnly(args);
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPlatformOptions(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddPlatformStorage(builder.Configuration, builder.Environment);

builder.Services.AddSingleton<AggregationMetrics>();
builder.Services.AddSingleton<ApiMetrics>();
builder.Services.AddHostedService<AggregationHostedService>();
// Runs after aggregation - each pass reads/aggregates ALL currently
// available raw samples before any of them ever become old enough to be
// evicted by this job, so rollups never depend on samples this job deletes.
builder.Services.AddHostedService<RetentionHostedService>();

// API key remains the default scheme so existing machine/telemetry-oriented
// endpoints (and current controller AuthenticationSchemes attributes) keep
// working. JWT Bearer is registered as an additional scheme for human-facing
// OIDC auth; later phases will switch tenant-management controllers to it.
var authenticationOptions = builder.Configuration
    .GetSection(AuthenticationOptions.SectionName)
    .Get<AuthenticationOptions>() ?? new AuthenticationOptions();

// Platform.Api-only Production fail-fast: human-facing host must not run
// anonymously unless operators explicitly opt out. Machine hosts (Ingestion)
// do not enforce this knob.
if (!authenticationOptions.Enabled
    && builder.Environment.IsProduction()
    && !authenticationOptions.AllowAnonymousProduction)
{
    throw new InvalidOperationException(
        "Authentication is disabled in Production but Authentication:AllowAnonymousProduction " +
        "is not true. Either configure Authentication:Enabled=true with Authority/Audience, " +
        "or set Authentication:AllowAnonymousProduction=true to explicitly allow anonymous " +
        "human-facing API access (not recommended).");
}

var authenticationBuilder = builder.Services
    .AddAuthentication(ApiKeyAuthenticationDefaults.SchemeName)
    .AddScheme<ApiKeyAuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationDefaults.SchemeName, _ => { });

if (authenticationOptions.Enabled)
{
    authenticationBuilder.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        // Authority triggers OIDC discovery (.well-known/openid-configuration)
        // and JWKS-based signature validation — no manual JWT parsing.
        options.Authority = authenticationOptions.Authority;
        options.Audience = authenticationOptions.Audience;
        options.RequireHttpsMetadata = authenticationOptions.RequireHttpsMetadata;
        // Keep JWT short names (iss/sub/role) so membership + policies stay
        // provider-neutral without depending on long WS-* claim URIs.
        options.MapInboundClaims = false;
    });
}

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<PlatformAdministratorEvaluator>();
builder.Services.AddSingleton<ITenantAuthorizationService, TenantAuthorizationService>();
builder.Services.AddSingleton<IInvitationClaimService, InvitationClaimService>();
builder.Services.AddSingleton<IAuthorizationHandler, TenantCreationAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, TenantMembershipAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, PlatformAdministratorAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, TenantMembershipAuthorizationResultHandler>();

builder.Services.AddAuthorization(options =>
{
    // Named policy for human-facing endpoints (JWT Bearer).
    // When OIDC is off the JWT scheme is not registered — deny cleanly
    // instead of throwing on an unknown scheme (would surface as 500).
    options.AddPolicy(PlatformAuthenticationPolicies.JwtBearer, policy =>
    {
        if (authenticationOptions.Enabled)
        {
            policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
            policy.RequireAuthenticatedUser();
        }
        else
        {
            policy.RequireAssertion(_ => false);
        }
    });

    // Tenant bootstrap: when OIDC is on, require JWT + TenantCreationOptions;
    // when off (dev/testing), allow anonymous self-service as before.
    options.AddPolicy(PlatformAuthenticationPolicies.TenantCreation, policy =>
    {
        if (authenticationOptions.Enabled)
        {
            policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
            policy.RequireAuthenticatedUser();
            policy.Requirements.Add(new TenantCreationRequirement());
        }
        else
        {
            policy.RequireAssertion(_ => true);
        }
    });

    // Tenant-scoped resources: API key (trusted tenant_id) and/or JWT membership
    // (or platform-administrator bypass for legacy-tenant operations).
    var tenantMemberSchemes = new List<string> { ApiKeyAuthenticationDefaults.SchemeName };
    if (authenticationOptions.Enabled)
    {
        tenantMemberSchemes.Add(JwtBearerDefaults.AuthenticationScheme);
    }

    options.AddPolicy(PlatformAuthenticationPolicies.TenantMember, policy =>
    {
        policy.AddAuthenticationSchemes(tenantMemberSchemes.ToArray());
        policy.RequireAuthenticatedUser();
        policy.Requirements.Add(new TenantMembershipRequirement());
    });

    // Explicit membership reconciliation / platform operator actions.
    // Never anonymous: when OIDC is off there is no JWT path to satisfy this.
    options.AddPolicy(PlatformAuthenticationPolicies.PlatformAdministrator, policy =>
    {
        if (authenticationOptions.Enabled)
        {
            policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
            policy.RequireAuthenticatedUser();
            policy.Requirements.Add(new PlatformAdministratorRequirement());
        }
        else
        {
            policy.RequireAssertion(_ => false);
        }
    });

    // API key lifecycle: API-key machine principals and/or JWT humans.
    // When OIDC is off, only the API-key scheme is registered (dev/bootstrap).
    options.AddPolicy(PlatformAuthenticationPolicies.ApiKeyManagement, policy =>
    {
        policy.AddAuthenticationSchemes(tenantMemberSchemes.ToArray());
        policy.RequireAuthenticatedUser();
    });
});

builder.Services.AddHealthChecks()
    .AddPlatformStorageHealthChecks();

// Anti-abuse control for the anonymous POST /v1/tenants bootstrap endpoint:
// there is no API key yet at that point, so the only practical throttle is
// per-client-IP. 5 requests/minute is generous for legitimate self-service
// signup while limiting automated abuse. Exceeding the limit returns 429.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimiterPolicies.TenantBootstrap, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

var corsOptions = builder.Configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>()
                  ?? new CorsOptions();
var allowedOrigins = corsOptions.AllowedOrigins ?? [];
if (allowedOrigins.Length == 0 && builder.Environment.IsDevelopment())
{
    // Local-dev default so Angular ng serve works out of the box.
    // Production/Staging must set Cors:AllowedOrigins explicitly.
    allowedOrigins =
    [
        "http://localhost:4200",
        "http://localhost:4600",
        "https://localhost:4200",
        "https://localhost:4600"
    ];
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("Angular", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                .AllowCredentials()
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
        else
        {
            policy.SetIsOriginAllowed(_ => false);
        }
    });
});

var app = builder.Build();

if (migrateOnly)
{
    var runner = app.Services.GetService<RelationalMigrationRunner>();
    if (runner is null)
    {
        app.Logger.LogError(
            "Cannot run --migrate: relational storage is not configured. " +
            "Set Storage:Provider to Sqlite or PostgreSql and ConnectionStrings:Platform.");
        return;
    }

    await runner.MigrateAsync();
    app.Logger.LogInformation("Migration job finished; exiting.");
    return;
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Explicit UseRouting() BEFORE UseAuthentication() is required here: since
// UseAuthentication() is called explicitly (not left to ASP.NET Core's
// implicit middleware insertion), the framework does NOT reliably guarantee
// endpoint routing has already run by this point. Without an explicit
// UseRouting() first, Context.GetEndpoint() inside ApiKeyAuthenticationHandler
// can be null for every request, making the IAuthorizeData check there
// unreliable and defeating the "NoResult() for anonymous endpoints" fix.
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
// IMPORTANT: the policy above is registered with a NAME ("Angular") via
// AddPolicy, not AddDefaultPolicy. The policy name must be passed explicitly.
app.UseCors("Angular");
app.UseRateLimiter();

app.UseDefaultFiles();
app.UseStaticFiles();

// Liveness: process is up (no dependency checks).
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/healthz", new HealthCheckOptions { Predicate = _ => false });
// Readiness: can serve traffic (includes DB when relational storage is used).
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description
            })
        };
        await context.Response.WriteAsJsonAsync(payload);
    }
});
app.MapControllers();
app.MapFallbackToFile("index.html");

// Start hosted services (including schema initializer).
await app.StartAsync();

var schemaStatus = app.Services.GetService<RelationalSchemaOptions>() is { } schema
    ? $"{schema.Provider}/{schema.Mode}"
    : "InMemory";
PlatformStartupDiagnostics.LogStartupSummary(
    app.Logger, app.Environment, app.Configuration, schemaStatus);

await app.WaitForShutdownAsync();

public partial class Program { }
