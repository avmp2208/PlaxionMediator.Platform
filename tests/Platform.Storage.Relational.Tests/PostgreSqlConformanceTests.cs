using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Storage.Relational;
using Platform.Storage.Tests;
using Xunit;

namespace Platform.Storage.Relational.Tests;

public sealed class PostgreSqlConformanceTests : StorageProviderConformanceTests
{
    private const string ConnectionStringEnvVar = "ConnectionStrings__Platform";
    private readonly string? _connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvVar);

    protected override async Task<StorageHandles> CreateStoresAsync()
    {
        // These tests require a real PostgreSQL instance, which isn't available
        // in most local/CI environments (e.g. GitHub-hosted runners without a
        // provisioned Postgres service). Rather than failing the build when the
        // env var isn't set, dynamically skip the test via Xunit.SkippableFact.
        Skip.If(
            string.IsNullOrEmpty(_connectionString),
            $"PostgreSql conformance tests require the '{ConnectionStringEnvVar}' environment variable to be set.");

        return await CreateFromConnectionStringAsync(_connectionString!, ensureCreated: true);
    }

    protected override async Task<StorageHandles?> RecreateStoresAsync(StorageHandles original)
    {
        if (string.IsNullOrEmpty(_connectionString)) return null;

        return await CreateFromConnectionStringAsync(_connectionString, ensureCreated: false);
    }

    protected override Task DisposeStoresAsync(StorageHandles handles)
    {
        return Task.CompletedTask;
    }

    private static async Task<StorageHandles> CreateFromConnectionStringAsync(string connectionString, bool ensureCreated)
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        var factory = new TestDbContextFactory(options);

        if (ensureCreated)
        {
            await using var db = await factory.CreateDbContextAsync();
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
        }

        return new StorageHandles(
            new RelationalMetricSampleStore(factory),
            new RelationalAggregateRollupStore(factory),
            new RelationalServiceCatalog(factory),
            new RelationalApiKeyStore(factory));
    }

    private sealed class TestDbContextFactory : IDbContextFactory<PlatformDbContext>
    {
        private readonly DbContextOptions<PlatformDbContext> _options;
        public TestDbContextFactory(DbContextOptions<PlatformDbContext> options) => _options = options;
        public PlatformDbContext CreateDbContext() => new(_options);
        public Task<PlatformDbContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(CreateDbContext());
    }
}
