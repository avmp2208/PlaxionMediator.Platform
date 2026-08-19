using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Platform.Hosting;
using Platform.Hosting.Configuration;
using Platform.Identity;
using Platform.Ingestion;
using Platform.Storage.Relational;
using Platform.Storage.Relational.Health;

var migrateOnly = MigrateCommand.IsMigrateOnly(args);
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPlatformOptions(builder.Configuration);

// Ingest payload guardrail (OTLP protobuf + JSON bridge).
var maxRequestBodyBytes = builder.Configuration.GetSection(IngestionOptions.SectionName)
    .GetValue<long?>("MaxRequestSizeBytes") ?? 1_048_576L;
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = maxRequestBodyBytes;
});
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maxRequestBodyBytes;
});

builder.Services.AddControllers();
builder.Services.AddPlatformStorage(builder.Configuration, builder.Environment);
builder.Services.AddSingleton<IngestionMetrics>();
builder.Services.AddSingleton<CumulativeMetricStateStore>();
builder.Services
    .AddAuthentication(ApiKeyAuthenticationDefaults.SchemeName)
    .AddScheme<ApiKeyAuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationDefaults.SchemeName, _ => { });
builder.Services.AddAuthorization();
builder.Services.AddOpenApi();

builder.Services.AddHealthChecks()
    .AddPlatformStorageHealthChecks();

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

// Liveness
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/healthz", new HealthCheckOptions { Predicate = _ => false });
// Readiness
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

// Start hosted services (including the storage provider's schema initializer).
await app.StartAsync();

var schemaStatus = app.Services.GetService<RelationalSchemaOptions>() is { } schema
    ? $"{schema.Provider}/{schema.Mode}"
    : "InMemory";
PlatformStartupDiagnostics.LogStartupSummary(
    app.Logger, app.Environment, app.Configuration, schemaStatus);

await app.WaitForShutdownAsync();

public partial class Program { }
