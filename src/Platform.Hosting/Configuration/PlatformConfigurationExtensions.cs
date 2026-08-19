using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Platform.Aggregation;

namespace Platform.Hosting.Configuration;

public static class PlatformConfigurationExtensions
{
    /// <summary>
    /// Registers strongly-typed options with data-annotation / IValidateOptions
    /// validation and ValidateOnStart so hosts fail fast on bad configuration.
    /// </summary>
    public static IServiceCollection AddPlatformOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<PlatformOptions>()
            .Bind(configuration.GetSection(PlatformOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<StorageOptions>, StorageOptionsValidator>();

        services.AddOptions<IngestionOptions>()
            .Bind(configuration.GetSection(IngestionOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<IngestionOptions>, IngestionOptionsValidator>();

        services.AddOptions<AggregationOptions>()
            .Bind(configuration.GetSection(AggregationOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AggregationOptions>, AggregationOptionsValidator>();

        services.AddOptions<RetentionOptions>()
            .Bind(configuration.GetSection("Retention"))
            .Validate(o => o.RawSampleRetention > TimeSpan.Zero,
                "Retention:RawSampleRetention must be a positive TimeSpan.")
            .Validate(o => o.CheckInterval > TimeSpan.Zero,
                "Retention:CheckInterval must be a positive TimeSpan.")
            .ValidateOnStart();

        services.AddOptions<CorsOptions>()
            .Bind(configuration.GetSection(CorsOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<SecurityOptions>()
            .Bind(configuration.GetSection(SecurityOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<ObservabilityOptions>()
            .Bind(configuration.GetSection(ObservabilityOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateOnStart();

        return services;
    }
}
