using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Platform.Aggregation;
using Platform.Api;
using Platform.Hosting;
using Platform.Hosting.Configuration;
using Platform.Identity;
using Platform.Storage.Relational;
using Platform.Storage.Relational.Health;
using CorsOptions = Platform.Hosting.Configuration.CorsOptions;

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

builder.Services
    .AddAuthentication(ApiKeyAuthenticationDefaults.SchemeName)
    .AddScheme<ApiKeyAuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationDefaults.SchemeName, _ => { });
builder.Services.AddAuthorization();

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

app.UseAuthentication();
app.UseAuthorization();
// IMPORTANT: the policy above is registered with a NAME ("Angular") via
// AddPolicy, not AddDefaultPolicy. The policy name must be passed explicitly.
app.UseCors("Angular");
app.UseRateLimiter();

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

// Start hosted services (including schema initializer).
await app.StartAsync();

var schemaStatus = app.Services.GetService<RelationalSchemaOptions>() is { } schema
    ? $"{schema.Provider}/{schema.Mode}"
    : "InMemory";
PlatformStartupDiagnostics.LogStartupSummary(
    app.Logger, app.Environment, app.Configuration, schemaStatus);

await app.WaitForShutdownAsync();

public partial class Program { }
