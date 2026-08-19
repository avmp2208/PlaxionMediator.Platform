using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using MySql.EntityFrameworkCore.Extensions;
using Npgsql;
using Platform.Storage.Relational.Infrastructure;

namespace Platform.Storage.Relational;

public static class RelationalStorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SQLite EF Core storage provider behind the
    /// provider-neutral Platform.Storage interfaces.
    /// </summary>
    public static IServiceCollection AddPlatformSqliteStorage(
        this IServiceCollection services,
        string connectionString,
        SchemaInitializationMode schemaMode = SchemaInitializationMode.Migrate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddSingleton(new RelationalSchemaOptions(RelationalProviderKind.Sqlite, schemaMode));

        // See SqlitePragmaConnectionInterceptor for why this is needed:
        // without it, SQLite's default rollback-journal mode makes writers
        // (ingestion, aggregation) block every reader (UI queries) for the
        // duration of each write.
        services.AddDbContextFactory<PlatformDbContext>((sp, options) =>
        {
            options.UseSqlite(connectionString, b =>
                b.MigrationsAssembly(typeof(PlatformDbContext).Assembly.FullName));
            options.AddInterceptors(new SqlitePragmaConnectionInterceptor());
            options.ReplaceService<IMigrationsAssembly, NamespaceFilteredMigrationsAssembly>();
        });

        return services.AddPlatformRelationalStores();
    }

    /// <summary>
    /// Registers the MySQL (MySql.EntityFrameworkCore) EF Core storage provider
    /// behind the provider-neutral Platform.Storage interfaces.
    /// Schema init uses EnsureCreated until a MySQL migrations assembly exists.
    /// </summary>
    public static IServiceCollection AddPlatformMySqlStorage(
        this IServiceCollection services,
        string connectionString,
        SchemaInitializationMode schemaMode = SchemaInitializationMode.EnsureCreated)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddSingleton(new RelationalSchemaOptions(RelationalProviderKind.MySql, schemaMode));

        services.AddDbContextFactory<PlatformDbContext>(options =>
            options.UseMySQL(connectionString));

        return services.AddPlatformRelationalStores();
    }

    /// <summary>
    /// Registers the PostgreSQL (Npgsql) EF Core storage provider behind the
    /// provider-neutral Platform.Storage interfaces.
    /// </summary>
    public static IServiceCollection AddPlatformPostgresStorage(
        this IServiceCollection services,
        string connectionString,
        SchemaInitializationMode schemaMode = SchemaInitializationMode.Migrate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddSingleton(new RelationalSchemaOptions(RelationalProviderKind.PostgreSql, schemaMode));

        services.AddDbContextFactory<PlatformDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString, b =>
                b.MigrationsAssembly(typeof(PlatformDbContext).Assembly.FullName));
            options.ReplaceService<IMigrationsAssembly, NamespaceFilteredMigrationsAssembly>();
        });

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
        services.AddSingleton<RelationalMigrationRunner>();

        return services;
    }
}

/// <summary>Identifies which relational engine was registered.</summary>
public enum RelationalProviderKind
{
    Sqlite,
    MySql,
    PostgreSql
}

/// <summary>Schema initialization settings captured at DI registration time.</summary>
public sealed record RelationalSchemaOptions(
    RelationalProviderKind Provider,
    SchemaInitializationMode Mode);

/// <summary>
/// Ensures the relational schema exists (via EF migrations or EnsureCreated)
/// according to <see cref="RelationalSchemaOptions"/> before the host accepts traffic.
/// </summary>
internal sealed class RelationalSchemaInitializer : IHostedService
{
    private readonly IDbContextFactory<PlatformDbContext> _dbFactory;
    private readonly RelationalSchemaOptions _schemaOptions;
    private readonly ILogger<RelationalSchemaInitializer> _logger;

    public RelationalSchemaInitializer(
        IDbContextFactory<PlatformDbContext> dbFactory,
        RelationalSchemaOptions schemaOptions,
        ILogger<RelationalSchemaInitializer> logger)
    {
        _dbFactory = dbFactory;
        _schemaOptions = schemaOptions;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        switch (_schemaOptions.Mode)
        {
            case SchemaInitializationMode.Migrate:
                _logger.LogInformation(
                    "Applying EF Core migrations for {Provider}...",
                    _schemaOptions.Provider);
                await db.Database.MigrateAsync(cancellationToken);
                _logger.LogInformation("EF Core migrations applied successfully.");
                break;

            case SchemaInitializationMode.ValidateOnly:
            {
                var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
                if (pending.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"Database has {pending.Count} pending EF Core migration(s) " +
                        $"({string.Join(", ", pending)}). " +
                        "Production/Staging does not auto-migrate. Run a one-shot migration job: " +
                        "dotnet <host>.dll --migrate");
                }

                // Ensure the database is reachable even when no migrations are pending.
                if (!await db.Database.CanConnectAsync(cancellationToken))
                {
                    throw new InvalidOperationException(
                        "Database is unreachable. Check ConnectionStrings:Platform and network connectivity.");
                }

                _logger.LogInformation(
                    "Schema validation OK for {Provider} (no pending migrations).",
                    _schemaOptions.Provider);
                break;
            }

            case SchemaInitializationMode.EnsureCreated:
                await EnsureCreatedWithLegacyColumnPatchesAsync(db, cancellationToken);
                break;

            case SchemaInitializationMode.None:
                _logger.LogInformation(
                    "Schema initialization skipped (mode=None) for {Provider}.",
                    _schemaOptions.Provider);
                break;
        }
    }

    private async Task EnsureCreatedWithLegacyColumnPatchesAsync(
        PlatformDbContext db,
        CancellationToken cancellationToken)
    {
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

        // Lightweight add-column patches for databases originally created via
        // EnsureCreated before RequestType existed. Migrations supersede this
        // for Sqlite/PostgreSQL; retained for MySQL EnsureCreated path only.
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
                var sql = db.Database.IsNpgsql()
                    ? $"ALTER TABLE {table} ADD COLUMN IF NOT EXISTS \"{column}\" {type} NULL"
                    : $"ALTER TABLE {table} ADD COLUMN {column} {type} NULL";

                await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
            }
            catch (SqliteException ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
            {
            }
            catch (MySqlException ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
            {
            }
            catch (PostgresException)
            {
            }
        }
    }

    private void LogBenignRace() =>
        _logger.LogInformation(
            "Database schema already created by another process; continuing (benign startup race).");

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Explicit migration runner used by the <c>--migrate</c> host argument.
/// Safe for single-instance job execution prior to scaling out replicas.
/// </summary>
public sealed class RelationalMigrationRunner
{
    private readonly IDbContextFactory<PlatformDbContext> _dbFactory;
    private readonly ILogger<RelationalMigrationRunner> _logger;

    public RelationalMigrationRunner(
        IDbContextFactory<PlatformDbContext> dbFactory,
        ILogger<RelationalMigrationRunner> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        _logger.LogInformation(
            "Running explicit migration job. PendingCount={PendingCount}, Pending={Pending}",
            pending.Count,
            pending.Count == 0 ? "(none)" : string.Join(", ", pending));
        await db.Database.MigrateAsync(cancellationToken);
        _logger.LogInformation("Explicit migration job completed successfully.");
    }
}
