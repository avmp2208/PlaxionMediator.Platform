using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Storage;
using Platform.Storage.Relational;
using Platform.Storage.Tests;
using Xunit;

namespace Platform.Storage.Relational.Tests;

public sealed class RelationalStorageConformanceTests : StorageProviderConformanceTests
{
    [Fact]
    public async Task AddPlatformSqliteStorage_EnablesWalMode_SoReadsAreNotBlockedByWrites()
    {
        // Regression test for the UI being unable to fetch data while the
        // aggregation/ingestion background writers were active: without WAL
        // mode, SQLite's default rollback-journal mode locks the whole
        // database file for readers during a writer's transaction.
        var path = Path.Combine(Path.GetTempPath(), $"platform-storage-wal-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        try
        {
            var services = new ServiceCollection();
            services.AddPlatformSqliteStorage(connectionString);
            using var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IDbContextFactory<PlatformDbContext>>();

            await using var db = await factory.CreateDbContextAsync();
            await db.Database.EnsureCreatedAsync();

            var connection = db.Database.GetDbConnection();
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode;";
            var journalMode = (string)(await command.ExecuteScalarAsync())!;

            Assert.Equal("wal", journalMode, ignoreCase: true);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                foreach (var extra in new[] { "-wal", "-shm" })
                {
                    if (File.Exists(path + extra))
                    {
                        File.Delete(path + extra);
                    }
                }
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }

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
            new RelationalMetricSampleStore(factory),
            new RelationalAggregateRollupStore(factory),
            new RelationalServiceCatalog(factory),
            new RelationalApiKeyStore(factory),
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
