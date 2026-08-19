extern alias IngestionHost;

using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Platform.Aggregation;
using Platform.Api;
using Platform.Identity;
using Platform.Storage;
using Platform.Storage.Relational;
using Xunit;

namespace Platform.Api.Tests;

/// <summary>
/// Proves Ingestion writes and Api reads the same persisted data via a
/// shared SQLite file (separate WebApplicationFactory hosts).
/// </summary>
public sealed class SharedSqlitePersistenceTests : IAsyncLifetime
{
    private string _dbPath = null!;
    private string _connectionString = null!;

    public Task InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"platform-shared-{Guid.NewGuid():N}.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch
        {
            // best effort
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task IngestionWrite_IsVisibleToApi_ViaSharedSqliteFile()
    {
        await using var ingestionFactory = new SqliteWebAppFactory<IngestionHost::Program>(_connectionString);
        await using var apiFactory = new SqliteWebAppFactory<Program>(_connectionString);

        // Force hosts to start (schema init via migrations).
        _ = ingestionFactory.Server;
        _ = apiFactory.Server;

        Assert.IsType<RelationalMetricSampleStore>(ingestionFactory.Services.GetRequiredService<IMetricSampleStore>());
        Assert.IsType<RelationalMetricSampleStore>(apiFactory.Services.GetRequiredService<IMetricSampleStore>());
        Assert.IsType<RelationalApiKeyStore>(apiFactory.Services.GetRequiredService<IApiKeyStore>());

        var tenantId = Guid.NewGuid();
        string apiKey;
        using (var scope = ingestionFactory.Services.CreateScope())
        {
            var keys = scope.ServiceProvider.GetRequiredService<ApiKeyService>();
            apiKey = (await keys.CreateAsync(tenantId, "shared-sqlite")).PlaintextKey;
        }

        // Prove the key is visible through the Api host's store (shared file).
        using (var scope = apiFactory.Services.CreateScope())
        {
            var apiKeys = scope.ServiceProvider.GetRequiredService<ApiKeyService>();
            var resolved = await apiKeys.ValidateAsync(apiKey);
            Assert.Equal(tenantId, resolved);
        }

        var ingestClient = ingestionFactory.CreateClient();
        ingestClient.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, apiKey);

        var ingestResponse = await ingestClient.PostAsJsonAsync("/v1/ingest", new
        {
            resource = new { serviceName = "shared-svc", environment = "prod" },
            metrics = new[]
            {
                new { metricName = "http.server.duration", value = 33.0, timeUnixNano = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000 }
            }
        });
        ingestResponse.EnsureSuccessStatusCode();

        using (var scope = apiFactory.Services.CreateScope())
        {
            var aggregator = ActivatorUtilities.CreateInstance<AggregationHostedService>(scope.ServiceProvider);
            await aggregator.RunOnceAsync(CancellationToken.None);
        }

        var apiClient = apiFactory.CreateClient();
        apiClient.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, apiKey);

        var services = await apiClient.GetFromJsonAsync<List<ServiceResponse>>($"/v1/tenants/{tenantId}/services");
        var service = Assert.Single(services!);
        Assert.Equal("shared-svc", service.Name);

        var metrics = await apiClient.GetFromJsonAsync<List<MetricRollupResponse>>(
            $"/v1/tenants/{tenantId}/services/{service.Id}/metrics?range=24h");
        Assert.NotEmpty(metrics!);
    }

    private sealed class SqliteWebAppFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint>
        where TEntryPoint : class
    {
        private readonly string _cs;

        public SqliteWebAppFactory(string cs) => _cs = cs;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Testing host (Sqlite is a local/test-only durable provider - not
            // permitted in Production/Staging/QA) with explicit durable Sqlite +
            // auto-migrate for the test. Configuration must be set before
            // Program runs AddPlatformStorage.
            builder.UseEnvironment("Testing");
            builder.UseSetting("Storage:Provider", "Sqlite");
            builder.UseSetting("ConnectionStrings:Platform", _cs);
            builder.UseSetting("Database:AutoMigrate", "true");
            builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost:4200");
        }
    }
}
