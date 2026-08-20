using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Platform.Hosting.Configuration;
using Xunit;

namespace Platform.Api.Tests;

/// <summary>
/// Unit tests for Authentication options fail-fast validation
/// (Production without OIDC requires explicit AllowAnonymousProduction).
/// </summary>
public sealed class AuthenticationOptionsTests
{
    [Fact]
    public void Development_DisabledWithoutOptOut_OptionsBindSuccessfully()
    {
        using var sp = BuildProvider(
            environmentName: "Development",
            new Dictionary<string, string?>
            {
                ["Authentication:Enabled"] = "false",
                ["Storage:Provider"] = "InMemory",
                ["Aggregation:Interval"] = "00:01:00",
                ["Aggregation:BucketSize"] = "00:01:00",
                ["Retention:RawSampleRetention"] = "7.00:00:00",
                ["Retention:CheckInterval"] = "00:05:00",
                ["Ingestion:MaxRequestSizeBytes"] = "1048576",
                ["Ingestion:MaxSamplesPerRequest"] = "5000",
                ["Ingestion:MaxMetricNamesPerRequest"] = "200"
            });

        var options = sp.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
        Assert.False(options.Enabled);
    }

    [Fact]
    public void Production_DisabledWithExplicitOptOut_HostStartsPastAuthGate()
    {
        // Storage still fails in Production with InMemory/missing durable CS;
        // assert the auth gate itself is satisfied by the opt-out.
        using var factory = new ConfigurableFactory(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Storage:Provider", "InMemory");
            builder.UseSetting("Authentication:Enabled", "false");
            builder.UseSetting("Authentication:AllowAnonymousProduction", "true");
            builder.UseSetting("Cors:AllowedOrigins:0", "https://app.example.com");
        });

        var ex = Assert.ThrowsAny<Exception>(() =>
        {
            _ = factory.Server;
        });

        Assert.DoesNotContain("AllowAnonymousProduction", GetFullMessage(ex), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("InMemory", GetFullMessage(ex), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Enabled_WithoutAuthority_FailsValidation()
    {
        using var sp = BuildProvider(
            environmentName: "Development",
            new Dictionary<string, string?>
            {
                ["Authentication:Enabled"] = "true",
                ["Authentication:Audience"] = "platform-api",
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
            _ = sp.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
        });

        Assert.Contains("Authority", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Enabled_WithAuthorityAndAudience_Succeeds()
    {
        using var sp = BuildProvider(
            environmentName: "Production",
            new Dictionary<string, string?>
            {
                ["Authentication:Enabled"] = "true",
                ["Authentication:Authority"] = "https://login.example.com/",
                ["Authentication:Audience"] = "platform-api",
                ["Authentication:RequireHttpsMetadata"] = "true",
                ["Storage:Provider"] = "InMemory",
                ["Aggregation:Interval"] = "00:01:00",
                ["Aggregation:BucketSize"] = "00:01:00",
                ["Retention:RawSampleRetention"] = "7.00:00:00",
                ["Retention:CheckInterval"] = "00:05:00",
                ["Ingestion:MaxRequestSizeBytes"] = "1048576",
                ["Ingestion:MaxSamplesPerRequest"] = "5000",
                ["Ingestion:MaxMetricNamesPerRequest"] = "200"
            });

        var options = sp.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
        Assert.True(options.Enabled);
        Assert.Equal("https://login.example.com/", options.Authority);
        Assert.Equal("platform-api", options.Audience);
    }

    [Fact]
    public void Production_Host_DisabledWithoutOptOut_FailsStartup()
    {
        using var factory = new ConfigurableFactory(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Storage:Provider", "PostgreSql");
            builder.UseSetting("ConnectionStrings:Platform", "Host=localhost;Database=x;Username=u;Password=p");
            builder.UseSetting("Authentication:Enabled", "false");
            builder.UseSetting("Authentication:AllowAnonymousProduction", "false");
            builder.UseSetting("Cors:AllowedOrigins:0", "https://app.example.com");
            // Avoid real DB migrate/connect during auth fail-fast check.
            builder.UseSetting("Database:AutoMigrate", "false");
            builder.UseSetting("Database:FailOnPendingMigrations", "false");
        });

        var ex = Assert.ThrowsAny<Exception>(() =>
        {
            _ = factory.Server;
        });

        Assert.Contains("AllowAnonymousProduction", GetFullMessage(ex), StringComparison.OrdinalIgnoreCase);
    }

    private static ServiceProvider BuildProvider(string environmentName, Dictionary<string, string?> values)
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        services.AddSingleton<IConfiguration>(config);
        services.AddPlatformOptions(config);
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment(environmentName));
        return services.BuildServiceProvider();
    }

    private static string GetFullMessage(Exception ex)
    {
        var parts = new List<string>();
        for (var e = ex; e is not null; e = e.InnerException)
        {
            parts.Add(e.Message);
        }

        return string.Join(" | ", parts);
    }

    private sealed class ConfigurableFactory : WebApplicationFactory<Program>
    {
        private readonly Action<IWebHostBuilder> _configure;

        public ConfigurableFactory(Action<IWebHostBuilder> configure) =>
            _configure = configure;

        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            _configure(builder);
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
