using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Platform.Storage.Sqlite;

public static class SqliteStorageServiceCollectionExtensions
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

        services.AddSingleton<IMetricSampleStore, SqliteMetricSampleStore>();
        services.AddSingleton<IAggregateRollupStore, SqliteAggregateRollupStore>();
        services.AddSingleton<IServiceCatalog, SqliteServiceCatalog>();
        services.AddSingleton<IApiKeyStore, SqliteApiKeyStore>();
        services.AddHostedService<SqliteSchemaInitializer>();

        return services;
    }
}

/// <summary>
/// Ensures the SQLite schema exists before the host starts accepting traffic.
/// </summary>
internal sealed class SqliteSchemaInitializer : IHostedService
{
    private readonly IDbContextFactory<PlatformDbContext> _dbFactory;
    private readonly ILogger<SqliteSchemaInitializer> _logger;

    public SqliteSchemaInitializer(IDbContextFactory<PlatformDbContext> dbFactory, ILogger<SqliteSchemaInitializer> logger)
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
            // Multiple Platform processes (Ingestion/Api/Aggregation) sharing one durable
            // SQLite file can race on EnsureCreated at startup: one process wins the CREATE
            // TABLE race, the other sees "table already exists". The schema itself is fine -
            // this is a benign concurrent-initialization race, not a real failure.
            _logger.LogInformation(
                "SQLite schema already created by another process; continuing (benign startup race).");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
