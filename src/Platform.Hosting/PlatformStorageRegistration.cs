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
    /// selected via "Storage:Provider") outside Development/Testing, and
    /// in-memory storage as the local/test fallback. Also registers
    /// <see cref="ApiKeyService"/>.
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
            if (string.Equals(provider, "MySql", StringComparison.OrdinalIgnoreCase))
            {
                services.AddPlatformMySqlStorage(cs);
            }
            else if (string.Equals(provider, "Postgres", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(provider, "PostgreSql", StringComparison.OrdinalIgnoreCase))
            {
                services.AddPlatformPostgresStorage(cs);
            }
            else
            {
                services.AddPlatformSqliteStorage(cs);
            }
        }
        else
        {
            services.AddPlatformInMemoryStorage();
        }

        services.AddSingleton<ApiKeyService>();
        return services;
    }
}
