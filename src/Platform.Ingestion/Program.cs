using Microsoft.AspNetCore.Http.Features;
using Platform.Hosting;
using Platform.Identity;
using Platform.Storage;

var builder = WebApplication.CreateBuilder(args);

// Ingest payload guardrail (OTLP protobuf + JSON bridge).
const long maxRequestBodyBytes = 1_048_576; // 1 MiB
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
builder.Services
    .AddAuthentication(ApiKeyAuthenticationDefaults.SchemeName)
    .AddScheme<ApiKeyAuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationDefaults.SchemeName, _ => { });
builder.Services.AddAuthorization();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

// Local-dev only: seed a deterministic API key bound to DevTenant so a
// developer can call /v1/ingest without first provisioning a real key.
// DevTenant.Id is never accepted directly from ingestion requests - it is
// only ever reached through this seeded, hashed API key.
// The plaintext itself is fixed (not randomly generated) so it survives
// process restarts of the in-memory dev store - otherwise a key copied
// into a sample app becomes invalid on every restart. This is a
// Development-only convenience; it is still written to a local file
// (never logged) rather than checked into source.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();

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

    // Idempotent: with a durable provider (e.g. SQLite) this seed runs on
    // every process restart, so skip re-creating the key once it exists
    // rather than inserting a duplicate row each time.
    var existing = await apiKeyService.ListAsync(DevTenant.Id);
    if (!existing.Any(k => k.Name == "local-dev-seed"))
    {
        var seeded = await apiKeyService.CreateWithPlaintextAsync(DevTenant.Id, "local-dev-seed", devPlaintextKey);
        var keyPath = Path.Combine(app.Environment.ContentRootPath, ".dev-api-key");
        try
        {
            // Best-effort: parallel WebApplicationFactory hosts (tests) may race on this path.
            await using var stream = new FileStream(
                keyPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.ReadWrite);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(seeded.PlaintextKey);
            app.Logger.LogInformation(
                "Seeded local-dev API key for DevTenant. Plaintext written to {KeyPath} (dev-only; value is never logged).",
                keyPath);
        }
        catch (IOException)
        {
            app.Logger.LogInformation(
                "Seeded local-dev API key for DevTenant (plaintext file unavailable due to concurrent access; value is never logged).");
        }
    }
    else
    {
        app.Logger.LogInformation("Local-dev API key for DevTenant already seeded; skipping.");
    }
}

await app.WaitForShutdownAsync();

public partial class Program { }
