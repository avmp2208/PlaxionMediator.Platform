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

// var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
// if (allowedOrigins.Length == 0 && builder.Environment.IsDevelopment())
// {
//     // Local-dev default: appsettings.Development.json/launchSettings.json do
//     // not currently set Cors:AllowedOrigins, which previously resulted in an
//     // empty WithOrigins() call - i.e. NO origin allowed, causing the Angular
//     // dashboard to fail every cross-origin request (including the anonymous
//     // POST /v1/tenants) with a CORS error. Default to the standard `ng serve`
//     // port plus a few other commonly-used local dev ports (`ng serve --port`
//     // is often changed, e.g. to 4600, when multiple Angular apps run on one
//     // machine) so local development works out of the box without needing to
//     // chase down a CORS error every time the port differs. Production/
//     // staging/qa must still set Cors:AllowedOrigins explicitly via
//     // configuration - this fallback only applies in Development.
//     allowedOrigins =
//     [
//         "http://localhost:4200",
//         "http://localhost:4600",
//         "https://localhost:4200",
//         "https://localhost:4600"
//     ];
// }
string[] allowedOrigins =
[
    "http://localhost:4200",
    "http://localhost:4600",
    "https://localhost:4200",
    "https://localhost:4600"
];
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
            policy.AllowAnyOrigin()
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
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
// IMPORTANT: the policy above is registered with a NAME ("Angular") via
// AddPolicy, not AddDefaultPolicy. Calling app.UseCors() with no argument
// applies the *default* policy, which was never registered here - so the
// CORS rules configured above were silently never applied, no matter what
// origins/ports were listed. The policy name must be passed explicitly.
app.UseCors("Angular");

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
