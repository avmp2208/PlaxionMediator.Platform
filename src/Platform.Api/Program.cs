using Platform.Aggregation;
using Platform.Hosting;
using Platform.Identity;
using Platform.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddPlatformStorage(builder.Configuration, builder.Environment);
builder.Services.AddHostedService(sp =>
    ActivatorUtilities.CreateInstance<AggregationHostedService>(sp));

builder.Services
    .AddAuthentication(ApiKeyAuthenticationDefaults.SchemeName)
    .AddScheme<ApiKeyAuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationDefaults.SchemeName, _ => { });
builder.Services.AddAuthorization();
builder.Services.AddHealthChecks();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyMethod()
            .WithHeaders(ApiKeyAuthenticationDefaults.HeaderName);
    });
});



var app = builder.Build();

// Local-dev only: seed the SAME deterministic dev API key that
// Platform.Ingestion seeds. In Development, each service defaults to its
// own in-memory storage, so without this the key issued/seeded by
// Ingestion would not exist in Api's store, and the Dashboard (which
// authenticates against Api) would reject it as "Invalid or revoked API
// key" even though it works fine against Ingestion. For true shared
// multi-process persistence, configure the Sqlite storage provider
// instead (see docs/local-dev-setup.md).
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseCors();

app.MapHealthChecks("/health");
app.MapHealthChecks("/healthz");
app.MapControllers();

// Start hosted services (including the storage provider's schema
// initializer, e.g. SqliteSchemaInitializer) BEFORE seeding the dev API
// key below. app.Run() alone would not guarantee hosted services have
// finished StartAsync yet, causing "no such table" failures against a
// durable provider the first time it runs against an empty database.
await app.StartAsync();

if (app.Environment.IsDevelopment())
{
    const string devPlaintextKey = "pk_dev0000000000000000000000000000000000000000000000000000dev0";
    var apiKeyService = app.Services.GetRequiredService<ApiKeyService>();

    // Idempotent: with shared durable storage, Platform.Ingestion may have
    // already seeded this exact key, and with a durable provider this also
    // runs on every process restart - skip re-creating it once it exists.
    var existing = await apiKeyService.ListAsync(DevTenant.Id);
    if (!existing.Any(k => k.Name == "local-dev-seed"))
    {
        await apiKeyService.CreateWithPlaintextAsync(DevTenant.Id, "local-dev-seed", devPlaintextKey);
        app.Logger.LogInformation(
            "Seeded local-dev API key for DevTenant (dev-only; value is never logged).");
    }
    else
    {
        app.Logger.LogInformation("Local-dev API key for DevTenant already seeded; skipping.");
    }
}

await app.WaitForShutdownAsync();

public partial class Program { }
