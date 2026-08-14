using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Platform.Identity;
using Platform.Storage;
using Platform.Storage.Sqlite;

namespace Platform.Hosting;

public static class PlatformStorageRegistration
{
    public const string ConnectionStringName = "Platform";

    /// <summary>
    /// Registers durable SQLite storage outside Development/Testing, and
    /// in-memory storage as the local/test fallback. Also registers
    /// <see cref="ApiKeyService"/>.
    /// </summary>
    public static IServiceCollection AddPlatformStorage(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var useSqlite = !environment.IsDevelopment()
                        && !environment.IsEnvironment("Testing")
                        && !string.IsNullOrWhiteSpace(configuration.GetConnectionString(ConnectionStringName));

        // Allow explicit override via config even in Development (useful for demos).
        var provider = configuration["Storage:Provider"];
        if (string.Equals(provider, "Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            useSqlite = true;
        }
        else if (string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            useSqlite = false;
        }

        if (useSqlite)
        {
            var cs = configuration.GetConnectionString(ConnectionStringName)
                     ?? "Data Source=platform.db";
            services.AddPlatformSqliteStorage(cs);
        }
        else
        {
            services.AddPlatformInMemoryStorage();
        }

        services.AddSingleton<ApiKeyService>();
        return services;
    }
}
