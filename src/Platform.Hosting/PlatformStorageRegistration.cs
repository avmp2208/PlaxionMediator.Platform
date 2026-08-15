using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
    /// </summary>
    public static IServiceCollection AddPlatformStorage(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var useDurableStorage = !environment.IsDevelopment()
                                 && !environment.IsEnvironment("Testing")
                                 && !string.IsNullOrWhiteSpace(configuration.GetConnectionString(ConnectionStringName));

        // Allow explicit override via config even in Development (useful for demos).
        var provider = configuration["Storage:Provider"];
        if (string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            useDurableStorage = false;
        }
        else if (!string.IsNullOrWhiteSpace(provider))
        {
            useDurableStorage = true;
        }

        if (useDurableStorage)
        {
            var cs = configuration.GetConnectionString(ConnectionStringName)
                     ?? "Data Source=platform.db";

            // "Sqlite" is the default durable provider when none is configured explicitly.
            var relationalProvider = ParseRelationalProvider(provider) ?? RelationalProvider.Sqlite;
            switch (relationalProvider)
            {
                case RelationalProvider.MySql:
                    services.AddPlatformMySqlStorage(cs);
                    break;
                case RelationalProvider.PostgreSql:
                    services.AddPlatformPostgresStorage(cs);
                    break;
                case RelationalProvider.Sqlite:
                default:
                    services.AddPlatformSqliteStorage(cs);
                    break;
            }
        }
        else
        {
            services.AddPlatformInMemoryStorage();
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

        if (string.Equals(provider, "Postgres", StringComparison.OrdinalIgnoreCase))
        {
            return RelationalProvider.PostgreSql;
        }

        return Enum.TryParse<RelationalProvider>(provider, ignoreCase: true, out var parsed)
            ? parsed
            : null;
    }
}
