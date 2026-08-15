extern alias IngestionHost;

using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

        // Force hosts to start (schema init).
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
            builder.UseEnvironment("Production");
            builder.ConfigureTestServices(services =>
            {
                // Replace whatever storage Program.cs registered with a SQLite
                // provider pointed at the shared file.
                RemoveStorage(services);
                services.AddPlatformSqliteStorage(_cs);
                services.RemoveAll<ApiKeyService>();
                services.AddSingleton<ApiKeyService>();
            });
        }

        private static void RemoveStorage(IServiceCollection services)
        {
            var types = new[]
            {
                typeof(IMetricSampleStore),
                typeof(IAggregateRollupStore),
                typeof(IServiceCatalog),
                typeof(IApiKeyStore),
                typeof(IDbContextFactory<PlatformDbContext>),
                typeof(PlatformDbContext)
            };

            var remove = services
                .Where(d => types.Contains(d.ServiceType)
                            || (d.ImplementationType is not null
                                && d.ImplementationType.Namespace == "Platform.Storage.Relational")
                            || d.ServiceType.FullName?.Contains("PlatformDbContext") == true
                            || d.ServiceType.FullName?.Contains("SqliteSchemaInitializer") == true
                            || (d.ImplementationType?.Name.Contains("SqliteSchemaInitializer") ?? false)
                            || d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)
                               && (d.ImplementationType?.Name.Contains("Sqlite") ?? false))
                .ToList();

            foreach (var d in remove)
            {
                services.Remove(d);
            }
        }
    }
}
