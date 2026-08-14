using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Platform.Storage;
using Platform.Storage.Sqlite;
using Platform.Storage.Tests;

namespace Platform.Storage.Sqlite.Tests;

public sealed class SqliteStorageConformanceTests : StorageProviderConformanceTests
{
    protected override async Task<StorageHandles> CreateStoresAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"platform-storage-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        var handles = await CreateFromConnectionStringAsync(connectionString, path);
        return handles;
    }

    protected override async Task<StorageHandles?> RecreateStoresAsync(StorageHandles original)
    {
        if (original is not SqliteHandles sqlite)
        {
            return null;
        }

        // Fresh factory against the same on-disk file — proves durable restart.
        return await CreateFromConnectionStringAsync(
            sqlite.ConnectionString,
            sqlite.FilePath,
            ensureCreated: false,
            ownsFile: false);
    }

    protected override Task DisposeStoresAsync(StorageHandles handles)
    {
        if (handles is SqliteHandles sqlite)
        {
            sqlite.Factory.Dispose();
            if (sqlite.OwnsFile)
            {
                try
                {
                    SqliteConnection.ClearAllPools();
                    if (File.Exists(sqlite.FilePath))
                    {
                        File.Delete(sqlite.FilePath);
                    }
                }
                catch
                {
                    // best-effort cleanup
                }
            }
        }

        return Task.CompletedTask;
    }

    private static async Task<SqliteHandles> CreateFromConnectionStringAsync(
        string connectionString,
        string path,
        bool ensureCreated = true,
        bool ownsFile = true)
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseSqlite(connectionString)
            .Options;
        var factory = new TestDbContextFactory(options);

        if (ensureCreated)
        {
            await using var db = await factory.CreateDbContextAsync();
            await db.Database.EnsureCreatedAsync();
        }

        return new SqliteHandles(
            new SqliteMetricSampleStore(factory),
            new SqliteAggregateRollupStore(factory),
            new SqliteServiceCatalog(factory),
            new SqliteApiKeyStore(factory),
            factory,
            connectionString,
            path,
            ownsFile);
    }

    private sealed record SqliteHandles(
        IMetricSampleStore Samples,
        IAggregateRollupStore Rollups,
        IServiceCatalog Catalog,
        IApiKeyStore ApiKeys,
        TestDbContextFactory Factory,
        string ConnectionString,
        string FilePath,
        bool OwnsFile) : StorageHandles(Samples, Rollups, Catalog, ApiKeys);

    private sealed class TestDbContextFactory : IDbContextFactory<PlatformDbContext>, IDisposable
    {
        private readonly DbContextOptions<PlatformDbContext> _options;

        public TestDbContextFactory(DbContextOptions<PlatformDbContext> options)
        {
            _options = options;
        }

        public PlatformDbContext CreateDbContext() => new(_options);

        public Task<PlatformDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());

        public void Dispose()
        {
            // nothing to dispose on options; file cleanup handled by test class
        }
    }
}
