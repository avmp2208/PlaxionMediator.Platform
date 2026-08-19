using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Platform.Hosting.Configuration;
using Platform.Identity;
using Platform.Storage;
using Platform.Storage.Relational;

namespace Platform.Hosting;

public static class PlatformStorageRegistration
{
    public const string ConnectionStringName = "Platform";

    /// <summary>
    /// Registers durable relational storage (SQLite, MySQL or PostgreSQL,
    /// selected via "Storage:Provider" and represented by <see cref="RelationalProvider"/>)
    /// outside Development/Testing, and in-memory storage as the local/test
    /// fallback. Also registers <see cref="ApiKeyService"/>.
    /// <para>
    /// Production/Staging/QA never silently fall back to in-memory storage,
    /// and may not use Sqlite either: a missing/invalid provider or missing
    /// connection string fails fast. Only PostgreSql or MySql are allowed
    /// outside local development/testing; InMemory and Sqlite are local-only.
    /// </para>
    /// </summary>
    public static IServiceCollection AddPlatformStorage(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var providerRaw = configuration["Storage:Provider"];
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        var isProdLike = IsProductionLike(environment);

        if (string.Equals(providerRaw, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            if (isProdLike)
            {
                throw new InvalidOperationException(
                    "Storage:Provider 'InMemory' is not allowed in Production/Staging. " +
                    "Configure a durable provider (Sqlite, PostgreSql, or MySql) and " +
                    "ConnectionStrings:Platform (env var ConnectionStrings__Platform).");
            }

            services.AddPlatformInMemoryStorage();
            services.AddSingleton<ApiKeyService>();
            return services;
        }

        // Unrecognized non-empty provider → fail fast (never silently fall back).
        if (!string.IsNullOrWhiteSpace(providerRaw))
        {
            var parsed = ParseRelationalProvider(providerRaw);
            if (parsed is null)
            {
                throw new InvalidOperationException(
                    $"Storage:Provider '{providerRaw}' is not supported. " +
                    "Supported values: InMemory, Sqlite, MySql, PostgreSql (alias: Postgres).");
            }
        }

        var useDurableStorage = isProdLike
                                || (!string.IsNullOrWhiteSpace(providerRaw)
                                    && !string.Equals(providerRaw, "InMemory", StringComparison.OrdinalIgnoreCase));

        // Development/Testing default: in-memory when provider unset.
        if (!useDurableStorage)
        {
            services.AddPlatformInMemoryStorage();
            services.AddSingleton<ApiKeyService>();
            return services;
        }

        // Durable path requires a connection string — no default file path in Production.
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            if (isProdLike)
            {
                throw new InvalidOperationException(
                    "ConnectionStrings:Platform is required when Storage:Provider is a durable " +
                    "provider (Sqlite/MySql/PostgreSql) or when running in Production/Staging. " +
                    "Set env var ConnectionStrings__Platform. In-memory fallback is disabled outside Development/Testing.");
            }

            // Development with explicit durable provider but no CS — local default file
            // relative to the process working directory (container-friendly, not user-profile).
            connectionString = "Data Source=platform.db";
        }

        var relationalProvider = ParseRelationalProvider(providerRaw) ?? RelationalProvider.Sqlite;

        if (isProdLike && relationalProvider == RelationalProvider.Sqlite)
        {
            throw new InvalidOperationException(
                "Storage:Provider 'Sqlite' is not allowed in Staging/QA/Production. " +
                "Sqlite and InMemory are local-development-only providers. Configure a " +
                "durable server provider (PostgreSql or MySql) via Storage:Provider and " +
                "ConnectionStrings:Platform (env var ConnectionStrings__Platform).");
        }

        var databaseOptions = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()
                              ?? new DatabaseOptions();

        // MySQL does not yet have a dedicated EF migrations assembly in this repo.
        // Keep EnsureCreated for MySQL only; Sqlite + PostgreSQL use migrations.
        SchemaInitializationMode migrationMode;
        if (relationalProvider == RelationalProvider.MySql)
        {
            migrationMode = SchemaInitializationMode.EnsureCreated;
        }
        else
        {
            migrationMode = ResolveMigrationMode(environment, databaseOptions);
        }

        switch (relationalProvider)
        {
            case RelationalProvider.MySql:
                services.AddPlatformMySqlStorage(connectionString, migrationMode);
                break;
            case RelationalProvider.PostgreSql:
                services.AddPlatformPostgresStorage(connectionString, migrationMode);
                break;
            case RelationalProvider.Sqlite:
            default:
                services.AddPlatformSqliteStorage(connectionString, migrationMode);
                break;
        }

        services.AddSingleton<ApiKeyService>();
        return services;
    }

    /// <summary>
    /// Parses the "Storage:Provider" configuration string into a
    /// <see cref="RelationalProvider"/>. Returns null when the value is
    /// null/empty or does not correspond to a known provider (e.g. "InMemory"),
    /// in which case in-memory storage should be used instead. Accepts the
    /// alias "Postgres" as a synonym for <see cref="RelationalProvider.PostgreSql"/>,
    /// and is case-insensitive.
    /// </summary>
    public static RelationalProvider? ParseRelationalProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return null;
        }

        if (string.Equals(provider, "Postgres", StringComparison.OrdinalIgnoreCase)
            || string.Equals(provider, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            return RelationalProvider.PostgreSql;
        }

        return Enum.TryParse<RelationalProvider>(provider, ignoreCase: true, out var parsed)
            ? parsed
            : null;
    }

    public static bool IsProductionLike(IHostEnvironment environment) =>
        environment.IsProduction()
        || environment.IsEnvironment("Staging")
        || environment.IsEnvironment("QA");

    public static SchemaInitializationMode ResolveMigrationMode(
        IHostEnvironment environment,
        DatabaseOptions databaseOptions)
    {
        if (databaseOptions.AutoMigrate == true)
        {
            return SchemaInitializationMode.Migrate;
        }

        if (databaseOptions.AutoMigrate == false)
        {
            return databaseOptions.FailOnPendingMigrations
                ? SchemaInitializationMode.ValidateOnly
                : SchemaInitializationMode.None;
        }

        // Default: auto-migrate in Development/Testing only.
        if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            return SchemaInitializationMode.Migrate;
        }

        return databaseOptions.FailOnPendingMigrations
            ? SchemaInitializationMode.ValidateOnly
            : SchemaInitializationMode.None;
    }
}
