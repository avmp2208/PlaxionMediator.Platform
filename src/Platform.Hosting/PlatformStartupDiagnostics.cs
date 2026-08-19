using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Platform.Hosting;

/// <summary>
/// Emits a single non-secret startup diagnostic line summarizing environment,
/// storage, aggregation, retention, and assembly version.
/// </summary>
public static class PlatformStartupDiagnostics
{
    public static void LogStartupSummary(
        ILogger logger,
        IHostEnvironment environment,
        IConfiguration configuration,
        string? schemaStatus = null)
    {
        var storageProvider = configuration["Storage:Provider"] ?? "(default)";
        var aggregationInterval = configuration["Aggregation:Interval"] ?? "00:01:00";
        var aggregationBucket = configuration["Aggregation:BucketSize"] ?? "00:01:00";
        var retentionWindow = configuration["Retention:RawSampleRetention"] ?? "7.00:00:00";
        var retentionCheck = configuration["Retention:CheckInterval"] ?? "00:05:00";
        var version = Assembly.GetEntryAssembly()
                           ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                           ?.InformationalVersion
                       ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
                       ?? "unknown";

        var hasConnectionString = !string.IsNullOrWhiteSpace(
            configuration.GetConnectionString(PlatformStorageRegistration.ConnectionStringName));

        logger.LogInformation(
            "Platform startup: Environment={Environment}, StorageProvider={StorageProvider}, " +
            "ConnectionStringConfigured={ConnectionStringConfigured}, SchemaStatus={SchemaStatus}, " +
            "AggregationInterval={AggregationInterval}, AggregationBucketSize={AggregationBucketSize}, " +
            "RetentionRawSampleWindow={RetentionWindow}, RetentionCheckInterval={RetentionCheck}, " +
            "Version={Version}",
            environment.EnvironmentName,
            storageProvider,
            hasConnectionString,
            schemaStatus ?? "n/a",
            aggregationInterval,
            aggregationBucket,
            retentionWindow,
            retentionCheck,
            version);
    }

    /// <summary>
    /// Resolves the effective storage provider label after registration rules are applied.
    /// </summary>
    public static string ResolveEffectiveStorageProvider(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var configured = configuration["Storage:Provider"];
        if (string.Equals(configured, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            return "InMemory";
        }

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            return "InMemory";
        }

        return "Sqlite";
    }
}
