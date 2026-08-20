using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Platform.Api.Controllers;
using Xunit;

namespace Platform.Api.Tests;

/// <summary>
/// Covers the anonymous GET /v1/platform/config runtime configuration endpoint
/// used by the Angular Dashboard at startup.
/// </summary>
public sealed class PlatformConfigEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task GetConfig_WhenAuthenticationDisabled_ReturnsEnabledFalseAndNoSecrets()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/platform/config");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<PlatformConfigController.PlatformConfigResponse>(JsonOptions);
        Assert.NotNull(payload);
        Assert.NotNull(payload!.Authentication);
        Assert.False(payload.Authentication.Enabled);
        Assert.Null(payload.Authentication.Authority);
        Assert.Null(payload.Authentication.ClientId);
        Assert.Contains("openid", payload.Authentication.Scopes);
        Assert.Equal("/auth/callback", payload.Authentication.RedirectPath);
        Assert.Equal("/", payload.Authentication.PostLogoutRedirectPath);

        // Public-safe: response must not look like it contains API keys / secrets.
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("clientSecret", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiKey", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pk_", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetConfig_WhenAuthenticationEnabled_ReturnsAuthorityAndClientId()
    {
        await using var factory = new ConfigurableFactory(builder =>
        {
            builder.UseSetting("Authentication:Enabled", "true");
            builder.UseSetting("Authentication:Authority", "https://login.example.test/");
            builder.UseSetting("Authentication:Audience", "platform-api");
            builder.UseSetting("Authentication:RequireHttpsMetadata", "false");
            builder.UseSetting("Dashboard:ApiBaseUrl", "https://api.example.test");
            builder.UseSetting("Dashboard:Oidc:ClientId", "platform-dashboard");
            // Scopes default from appsettings (openid/profile/email); avoid indexed
            // UseSetting which appends onto the existing array.
            builder.UseSetting("Dashboard:Oidc:RedirectPath", "/auth/callback");
            builder.UseSetting("Dashboard:Oidc:PostLogoutRedirectPath", "/");
            builder.UseSetting("Storage:Provider", "InMemory");
        });

        var client = factory.CreateClient();
        var response = await client.GetAsync("/v1/platform/config");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<PlatformConfigController.PlatformConfigResponse>(JsonOptions);
        Assert.NotNull(payload);
        Assert.Equal("https://api.example.test", payload!.ApiBaseUrl);
        Assert.True(payload.Authentication.Enabled);
        Assert.Equal("https://login.example.test/", payload.Authentication.Authority);
        Assert.Equal("platform-dashboard", payload.Authentication.ClientId);
        Assert.Contains("openid", payload.Authentication.Scopes);
        Assert.Contains("profile", payload.Authentication.Scopes);
        Assert.Contains("email", payload.Authentication.Scopes);
    }

    [Fact]
    public async Task GetConfig_IsAnonymous_NoAuthHeaderRequired()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        // Explicitly no auth headers.
        client.DefaultRequestHeaders.Clear();

        var response = await client.GetAsync("/v1/platform/config");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed class ConfigurableFactory : WebApplicationFactory<Program>
    {
        private readonly Action<IWebHostBuilder> _configure;

        public ConfigurableFactory(Action<IWebHostBuilder> configure)
        {
            _configure = configure;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder) => _configure(builder);
    }
}
