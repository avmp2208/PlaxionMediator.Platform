using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Platform.Storage.Relational;

/// <summary>
/// Design-time factory for <c>dotnet ef migrations</c>.
/// Usage examples:
/// <code>
/// dotnet ef migrations add Initial --context PlatformDbContext --project src/Platform.Storage.Relational --output-dir Migrations/Sqlite -- --provider Sqlite
/// dotnet ef migrations add Initial --context PlatformDbContext --project src/Platform.Storage.Relational --output-dir Migrations/PostgreSql -- --provider PostgreSql
/// </code>
/// </summary>
public sealed class PlatformDbContextFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args)
    {
        var provider = "Sqlite";
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--provider", StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Length)
            {
                provider = args[i + 1];
            }
        }

        var builder = new DbContextOptionsBuilder<PlatformDbContext>();

        if (string.Equals(provider, "PostgreSql", StringComparison.OrdinalIgnoreCase)
            || string.Equals(provider, "Postgres", StringComparison.OrdinalIgnoreCase))
        {
            builder.UseNpgsql("Host=localhost;Database=platform;Username=platform;Password=platform");
        }
        else if (string.Equals(provider, "MySql", StringComparison.OrdinalIgnoreCase))
        {
            builder.UseMySQL("Server=localhost;Database=platform;User=platform;Password=platform");
        }
        else
        {
            builder.UseSqlite("Data Source=design-time.db");
        }

        return new PlatformDbContext(builder.Options);
    }
}
