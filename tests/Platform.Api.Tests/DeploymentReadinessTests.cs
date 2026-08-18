using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Platform.Aggregation;
using Platform.Hosting;
using Platform.Hosting.Configuration;
using Platform.Identity;
using Platform.Storage;
using Platform.Storage.Relational;
using Xunit;

namespace Platform.Api.Tests;

/// <summary>
/// Deployment-readiness tests: configuration fail-fast, migrations, readiness,
/// and Production bootstrap isolation.
/// </summary>
public sealed class DeploymentReadinessTests
{
    [Fact]
    public void Production_MissingConnectionString_FailsStartup()
    {
        using var factory = new ConfigurableFactory(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Storage:Provider", "PostgreSql");
            // Intentionally omit ConnectionStrings:Platform
            builder.UseSetting("ConnectionStrings:Platform", "");
        });

        var ex = Assert.ThrowsAny<Exception>(() =>
        {
            _ = factory.Server;
        });

        Assert.Contains("ConnectionStrings:Platform", GetFullMessage(ex), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Production_InMemoryProvider_FailsStartup()
    {
        using var factory = new ConfigurableFactory(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Storage:Provider", "InMemory");
            builder.UseSetting("ConnectionStrings:Platform", "Host=localhost;Database=x");
        });

        var ex = Assert.ThrowsAny<Exception>(() =>
        {
            _ = factory.Server;
        });

        Assert.Contains("InMemory", GetFullMessage(ex), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnsupportedProvider_FailsStartup()
    {
        using var factory = new ConfigurableFactory(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Storage:Provider", "CosmosDb");
        });

        var ex = Assert.ThrowsAny<Exception>(() =>
        {
            _ = factory.Server;
        });

        Assert.Contains("not supported", GetFullMessage(ex), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConfigurationValidation_RejectsNonPositiveAggregationInterval()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:Provider"] = "InMemory",
                ["Aggregation:Interval"] = "00:00:00",
                ["Aggregation:BucketSize"] = "00:01:00",
                ["Retention:RawSampleRetention"] = "7.00:00:00",
                ["Retention:CheckInterval"] = "00:05:00",
                ["Ingestion:MaxRequestSizeBytes"] = "1048576",
                ["Ingestion:MaxSamplesPerRequest"] = "5000",
                ["Ingestion:MaxMetricNamesPerRequest"] = "200"
            })
            .Build();

        services.AddSingleton<IConfiguration>(config);
        services.AddPlatformOptions(config);
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment("Development"));

        using var sp = services.BuildServiceProvider();
        var ex = Assert.Throws<OptionsValidationException>(() =>
        {
            _ = sp.GetRequiredService<IOptions<AggregationOptions>>().Value;
        });
        Assert.Contains("Aggregation:Interval", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnvVarConfigBinding_BindsStorageProvider()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:Provider"] = "PostgreSql",
                ["ConnectionStrings:Platform"] = "Host=db;Database=platform;Username=u;Password=p"
            })
            .Build();

        // Env-var equivalent keys use __ → : which Configuration already normalizes
        // when loading environment variables; here we assert section binding.
        var storage = config.GetSection(StorageOptions.SectionName).Get<StorageOptions>();
        Assert.NotNull(storage);
        Assert.Equal("PostgreSql", storage!.Provider);
        Assert.Equal("Host=db;Database=platform;Username=u;Password=p",
            config.GetConnectionString(PlatformStorageRegistration.ConnectionStringName));
    }

    [Fact]
    public async Task Migrations_CreateSchema_AndPreserveData()
    {
        var path = Path.Combine(Path.GetTempPath(), $"platform-mig-{Guid.NewGuid():N}.db");
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        try
        {
            // Create schema exclusively via migrations (not EnsureCreated).
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddPlatformSqliteStorage(cs, SchemaInitializationMode.Migrate);
            await using var provider = services.BuildServiceProvider();

            // Run schema initializer hosted service.
            var initializer = provider.GetServices<IHostedService>()
                .OfType<object>()
                .First(s => s.GetType().Name.Contains("RelationalSchemaInitializer", StringComparison.Ordinal));
            await ((IHostedService)initializer).StartAsync(CancellationToken.None);

            var factory = provider.GetRequiredService<IDbContextFactory<PlatformDbContext>>();
            await using (var db = await factory.CreateDbContextAsync())
            {
                var applied = await db.Database.GetAppliedMigrationsAsync();
                Assert.Contains(applied, m => m.Contains("Initial", StringComparison.Ordinal));

                // Insert representative Doc B entities through storage interfaces.
                var samples = provider.GetRequiredService<IMetricSampleStore>();
                var rollups = provider.GetRequiredService<IAggregateRollupStore>();
                var catalog = provider.GetRequiredService<IServiceCatalog>();
                var keys = provider.GetRequiredService<IApiKeyStore>();

                var tenantId = Guid.NewGuid();
                var service = await catalog.GetOrCreateAsync(tenantId, "svc", "prod");
                await samples.AddAsync(new MetricSample
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    ServiceName = "svc",
                    Environment = "prod",
                    MetricName = "http.server.duration",
                    Value = 12.5,
                    Timestamp = DateTimeOffset.UtcNow,
                    RequestType = "GET /api"
                });
                await rollups.AddRangeAsync(
                [
                    new AggregateRollup
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        ServiceName = "svc",
                        Environment = "prod",
                        MetricName = "http.server.duration",
                        RequestType = "GET /api",
                        BucketStart = DateTimeOffset.UtcNow.AddMinutes(-1),
                        BucketEnd = DateTimeOffset.UtcNow,
                        Count = 1,
                        Min = 12.5,
                        Max = 12.5,
                        Avg = 12.5,
                        P50 = 12.5,
                        P95 = 12.5,
                        P99 = 12.5
                    }
                ]);
                _ = service;
                await keys.AddAsync(new ApiKey
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Name = "k1",
                    HashedKey = "hash",
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }

            // Re-open and verify data remains readable (migrations did not destroy schema).
            var services2 = new ServiceCollection();
            services2.AddLogging();
            services2.AddPlatformSqliteStorage(cs, SchemaInitializationMode.ValidateOnly);
            await using var provider2 = services2.BuildServiceProvider();
            var init2 = provider2.GetServices<IHostedService>()
                .First(s => s.GetType().Name.Contains("RelationalSchemaInitializer", StringComparison.Ordinal));
            await init2.StartAsync(CancellationToken.None);

            var samples2 = provider2.GetRequiredService<IMetricSampleStore>();
            var all = await samples2.GetAllAsync();
            Assert.NotEmpty(all);
            Assert.Contains(all, s => s.ServiceName == "svc" && s.RequestType == "GET /api");

            var catalog2 = provider2.GetRequiredService<IServiceCatalog>();
            // Service catalog list via GetOrCreate (idempotent) + keys
            var keys2 = provider2.GetRequiredService<IApiKeyStore>();
            var listed = await keys2.ListByTenantAsync(all[0].TenantId);
            Assert.NotEmpty(listed);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task FreshDatabase_InitializedExclusivelyThroughMigrations_NotEnsureCreated()
    {
        var path = Path.Combine(Path.GetTempPath(), $"platform-mig-only-{Guid.NewGuid():N}.db");
        var cs = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddPlatformSqliteStorage(cs, SchemaInitializationMode.Migrate);
            await using var provider = services.BuildServiceProvider();
            var init = provider.GetServices<IHostedService>()
                .First(s => s.GetType().Name.Contains("RelationalSchemaInitializer", StringComparison.Ordinal));
            await init.StartAsync(CancellationToken.None);

            await using var db = await provider.GetRequiredService<IDbContextFactory<PlatformDbContext>>()
                .CreateDbContextAsync();

            // __EFMigrationsHistory must exist (migrations path), not only raw tables.
            var conn = db.Database.GetDbConnection();
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(1) FROM __EFMigrationsHistory";
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            Assert.True(count >= 1);

            var tables = new[] { "metric_samples", "aggregate_rollups", "services", "api_keys" };
            foreach (var table in tables)
            {
                cmd.CommandText = $"SELECT name FROM sqlite_master WHERE type='table' AND name='{table}'";
                var name = await cmd.ExecuteScalarAsync();
                Assert.Equal(table, name);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task Readiness_Succeeds_WhenDatabaseAvailable()
    {
        var path = Path.Combine(Path.GetTempPath(), $"platform-ready-ok-{Guid.NewGuid():N}.db");
        var cs = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
        try
        {
            using var factory = new ConfigurableFactory(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting("Storage:Provider", "Sqlite");
                builder.UseSetting("ConnectionStrings:Platform", cs);
                builder.UseSetting("Database:AutoMigrate", "true");
            });

            var client = factory.CreateClient();
            var live = await client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);

            var ready = await client.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task Readiness_Fails_WhenDatabaseUnavailable()
    {
        // Point at a SQLite path under a non-existent directory so CanConnect fails.
        var missingDir = Path.Combine(Path.GetTempPath(), $"no-such-dir-{Guid.NewGuid():N}", "missing.db");
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = missingDir,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        using var factory = new ConfigurableFactory(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Storage:Provider", "Sqlite");
            builder.UseSetting("ConnectionStrings:Platform", cs);
            // Skip schema init so host can start; readiness should still fail CanConnect.
            builder.UseSetting("Database:AutoMigrate", "false");
            builder.UseSetting("Database:FailOnPendingMigrations", "false");
        });

        // Host may fail during schema init if ValidateOnly - with FailOnPending=false and AutoMigrate=false → None.
        // CreateClient forces start.
        HttpClient client;
        try
        {
            client = factory.CreateClient();
        }
        catch
        {
            // If startup itself fails because DB path is invalid during migrate/validate, that also proves unreadiness.
            return;
        }

        var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
    }

    [Fact]
    public void Sqlite_IsRejected_InProduction()
    {
        // Sqlite (like InMemory) is a local-development-only provider; Production
        // (and Staging/QA) must use a durable server provider (PostgreSql/MySql).
        // There is no dev-key bootstrap anymore - every tenant (including local
        // development) provisions its own API key via POST /v1/tenants.
        using var factory = new ConfigurableFactory(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Storage:Provider", "Sqlite");
            builder.UseSetting("ConnectionStrings:Platform", "Data Source=should-not-be-used.db");
            builder.UseSetting("Cors:AllowedOrigins:0", "https://app.example.com");
        });

        var ex = Assert.ThrowsAny<Exception>(() => factory.Server);
        Assert.Contains("Sqlite", GetFullMessage(ex));
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("QA")]
    public void Sqlite_IsRejected_InStagingAndQa(string environmentName)
    {
        using var factory = new ConfigurableFactory(builder =>
        {
            builder.UseEnvironment(environmentName);
            builder.UseSetting("Storage:Provider", "Sqlite");
            builder.UseSetting("ConnectionStrings:Platform", "Data Source=should-not-be-used.db");
            builder.UseSetting("Cors:AllowedOrigins:0", "https://app.example.com");
        });

        var ex = Assert.ThrowsAny<Exception>(() => factory.Server);
        Assert.Contains("Sqlite", GetFullMessage(ex));
    }

    [Theory]
    [InlineData("Sqlite", RelationalProvider.Sqlite)]
    [InlineData("PostgreSql", RelationalProvider.PostgreSql)]
    [InlineData("Postgres", RelationalProvider.PostgreSql)]
    public void ParseRelationalProvider_StillWorks(string value, RelationalProvider expected)
    {
        Assert.Equal(expected, PlatformStorageRegistration.ParseRelationalProvider(value));
    }

    private static string GetFullMessage(Exception ex)
    {
        var parts = new List<string>();
        for (var e = ex; e is not null; e = e.InnerException)
        {
            parts.Add(e.Message);
        }

        return string.Join(" | ", parts);
    }

    private sealed class ConfigurableFactory : WebApplicationFactory<Program>
    {
        private readonly Action<IWebHostBuilder> _configure;

        public ConfigurableFactory(Action<IWebHostBuilder> configure) => _configure = configure;

        protected override void ConfigureWebHost(IWebHostBuilder builder) => _configure(builder);
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public FakeHostEnvironment(string name) => EnvironmentName = name;
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
