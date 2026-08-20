using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Platform.Hosting.Configuration;
using Xunit;

namespace Platform.Api.Tests;

public sealed class PlatformAdministratorOptionsTests
{
    [Fact]
    public void DefaultMode_Disabled_BindsSuccessfully()
    {
        using var sp = BuildProvider(new Dictionary<string, string?>
        {
            ["Authorization:PlatformAdministrator:Mode"] = "Disabled",
            ["Storage:Provider"] = "InMemory",
            ["Aggregation:Interval"] = "00:01:00",
            ["Aggregation:BucketSize"] = "00:01:00",
            ["Retention:RawSampleRetention"] = "7.00:00:00",
            ["Retention:CheckInterval"] = "00:05:00",
            ["Ingestion:MaxRequestSizeBytes"] = "1048576",
            ["Ingestion:MaxSamplesPerRequest"] = "5000",
            ["Ingestion:MaxMetricNamesPerRequest"] = "200"
        });

        var options = sp.GetRequiredService<IOptions<PlatformAdministratorOptions>>().Value;
        Assert.Equal(PlatformAdministratorMode.Disabled, options.Mode);
    }

    [Fact]
    public void RoleMode_WithoutRequiredRole_FailsValidation()
    {
        using var sp = BuildProvider(new Dictionary<string, string?>
        {
            ["Authorization:PlatformAdministrator:Mode"] = "Role",
            ["Storage:Provider"] = "InMemory",
            ["Aggregation:Interval"] = "00:01:00",
            ["Aggregation:BucketSize"] = "00:01:00",
            ["Retention:RawSampleRetention"] = "7.00:00:00",
            ["Retention:CheckInterval"] = "00:05:00",
            ["Ingestion:MaxRequestSizeBytes"] = "1048576",
            ["Ingestion:MaxSamplesPerRequest"] = "5000",
            ["Ingestion:MaxMetricNamesPerRequest"] = "200"
        });

        var ex = Assert.Throws<OptionsValidationException>(() =>
        {
            _ = sp.GetRequiredService<IOptions<PlatformAdministratorOptions>>().Value;
        });

        Assert.Contains("RequiredRole", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ClaimMode_WithoutClaimType_FailsValidation()
    {
        using var sp = BuildProvider(new Dictionary<string, string?>
        {
            ["Authorization:PlatformAdministrator:Mode"] = "Claim",
            ["Storage:Provider"] = "InMemory",
            ["Aggregation:Interval"] = "00:01:00",
            ["Aggregation:BucketSize"] = "00:01:00",
            ["Retention:RawSampleRetention"] = "7.00:00:00",
            ["Retention:CheckInterval"] = "00:05:00",
            ["Ingestion:MaxRequestSizeBytes"] = "1048576",
            ["Ingestion:MaxSamplesPerRequest"] = "5000",
            ["Ingestion:MaxMetricNamesPerRequest"] = "200"
        });

        var ex = Assert.Throws<OptionsValidationException>(() =>
        {
            _ = sp.GetRequiredService<IOptions<PlatformAdministratorOptions>>().Value;
        });

        Assert.Contains("RequiredClaimType", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RoleMode_WithRequiredRole_Succeeds()
    {
        using var sp = BuildProvider(new Dictionary<string, string?>
        {
            ["Authorization:PlatformAdministrator:Mode"] = "Role",
            ["Authorization:PlatformAdministrator:RequiredRole"] = "platform-admin",
            ["Storage:Provider"] = "InMemory",
            ["Aggregation:Interval"] = "00:01:00",
            ["Aggregation:BucketSize"] = "00:01:00",
            ["Retention:RawSampleRetention"] = "7.00:00:00",
            ["Retention:CheckInterval"] = "00:05:00",
            ["Ingestion:MaxRequestSizeBytes"] = "1048576",
            ["Ingestion:MaxSamplesPerRequest"] = "5000",
            ["Ingestion:MaxMetricNamesPerRequest"] = "200"
        });

        var options = sp.GetRequiredService<IOptions<PlatformAdministratorOptions>>().Value;
        Assert.Equal(PlatformAdministratorMode.Role, options.Mode);
        Assert.Equal("platform-admin", options.RequiredRole);
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> values)
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        services.AddSingleton<IConfiguration>(config);
        services.AddPlatformOptions(config);
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment("Development"));
        return services.BuildServiceProvider();
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public FakeHostEnvironment(string name) => EnvironmentName = name;
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
