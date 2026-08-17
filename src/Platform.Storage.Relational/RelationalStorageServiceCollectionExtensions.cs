using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using MySql.EntityFrameworkCore.Extensions;
using Npgsql;

namespace Platform.Storage.Relational;

public static class RelationalStorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SQLite EF Core storage provider behind the
    /// provider-neutral Platform.Storage interfaces. Ensures the schema is
    /// created at startup.
    /// </summary>
    public static IServiceCollection AddPlatformSqliteStorage(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContextFactory<PlatformDbContext>(options =>
            options.UseSqlite(connectionString));

        return services.AddPlatformRelationalStores();
    }

    /// <summary>
    /// Registers the MySQL (MySql.EntityFrameworkCore) EF Core storage provider
    /// behind the provider-neutral Platform.Storage interfaces. Ensures the
    /// schema is created at startup.
    /// </summary>
    public static IServiceCollection AddPlatformMySqlStorage(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContextFactory<PlatformDbContext>(options =>
            options.UseMySQL(connectionString));

        return services.AddPlatformRelationalStores();
    }

    /// <summary>
    /// Registers the PostgreSQL (Npgsql) EF Core storage provider behind the
    /// provider-neutral Platform.Storage interfaces. Ensures the schema is
    /// created at startup.
    /// </summary>
    public static IServiceCollection AddPlatformPostgresStorage(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContextFactory<PlatformDbContext>(options =>
            options.UseNpgsql(connectionString));

        return services.AddPlatformRelationalStores();
    }

    private static IServiceCollection AddPlatformRelationalStores(this IServiceCollection services)
    {
        services.AddSingleton<StorageMetrics>();
        services.AddSingleton<IMetricSampleStore, RelationalMetricSampleStore>();
        services.AddSingleton<IAggregateRollupStore, RelationalAggregateRollupStore>();
        services.AddSingleton<IServiceCatalog, RelationalServiceCatalog>();
        services.AddSingleton<IApiKeyStore, RelationalApiKeyStore>();
        services.AddHostedService<RelationalSchemaInitializer>();

        return services;
    }
}

/// <summary>
/// Ensures the relational schema (SQLite, MySQL or PostgreSQL) exists before
/// the host starts accepting traffic.
/// </summary>
internal sealed class RelationalSchemaInitializer : IHostedService
{
    private readonly IDbContextFactory<PlatformDbContext> _dbFactory;
    private readonly ILogger<RelationalSchemaInitializer> _logger;

    public RelationalSchemaInitializer(IDbContextFactory<PlatformDbContext> dbFactory, ILogger<RelationalSchemaInitializer> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            await db.Database.EnsureCreatedAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1 &&
                                          ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            LogBenignRace();
        }
        catch (MySqlException ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            LogBenignRace();
        }
        catch (PostgresException ex) when (string.Equals(ex.SqlState, "42P07", StringComparison.Ordinal))
        {
            LogBenignRace();
        }

        // There is no EF Core migrations system in this project (schema is
        // created via EnsureCreated only). EnsureCreated does NOT alter an
        // already-existing table's columns, so a database file created by
        // a previous build (before a new nullable column such as
        // RequestType was added to the canonical model) will be missing
        // that column and every query against it fails with
        // "no such column" (SQLite) / equivalent errors on MySQL/Postgres.
        // As a lightweight, migration-free remedy for the MVP, best-effort
        // "add column if missing" statements are attempted here for every
        // column that has been added after the initial schema. This is
        // idempotent and safe to run on every startup.
        await AddColumnIfMissingAsync(db, cancellationToken);
    }

    private async Task AddColumnIfMissingAsync(PlatformDbContext db, CancellationToken cancellationToken)
    {
        var statements = new[]
        {
            ("metric_samples", "RequestType", "TEXT"),
            ("aggregate_rollups", "RequestType", "TEXT")
        };

        foreach (var (table, column, type) in statements)
        {
            try
            {
                // Postgres supports "ADD COLUMN IF NOT EXISTS" directly.
                // SQLite/MySQL do not, so those rely on catching the
                // "duplicate column" error below instead.
                var sql = db.Database.IsNpgsql()
                    ? $"ALTER TABLE {table} ADD COLUMN IF NOT EXISTS \"{column}\" {type} NULL"
                    : $"ALTER TABLE {table} ADD COLUMN {column} {type} NULL";

                await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
            }
            catch (SqliteException ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
            {
                // Column already present; nothing to do.
            }
            catch (MySqlException ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
            {
                // Column already present; nothing to do.
            }
            catch (PostgresException)
            {
                // IF NOT EXISTS above already makes this a no-op when present;
                // swallow any remaining benign race/duplicate errors.
            }
        }
    }

    private void LogBenignRace() =>
        // Multiple Platform processes (Ingestion/Api/Aggregation) sharing one durable
        // database can race on EnsureCreated at startup: one process wins the CREATE
        // TABLE race, the other sees "table already exists". The schema itself is fine -
        // this is a benign concurrent-initialization race, not a real failure.
        _logger.LogInformation(
            "Database schema already created by another process; continuing (benign startup race).");

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
