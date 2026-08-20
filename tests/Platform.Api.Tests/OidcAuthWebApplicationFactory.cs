using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Platform.Api.Authorization;
using Platform.Identity;

namespace Platform.Api.Tests;

/// <summary>
/// WebApplicationFactory with Authentication:Enabled and a test JWT scheme
/// so tenant-creation / membership authorization can be integration-tested
/// without OIDC discovery against a real authority.
/// </summary>
public sealed class OidcAuthWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly Action<IWebHostBuilder>? _configure;

    public OidcAuthWebApplicationFactory(Action<IWebHostBuilder>? configure = null)
    {
        _configure = configure;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Authentication:Enabled", "true");
        builder.UseSetting("Authentication:Authority", "https://login.test/");
        builder.UseSetting("Authentication:Audience", "platform-api");
        builder.UseSetting("Authentication:RequireHttpsMetadata", "false");
        builder.UseSetting("Authorization:TenantCreation:Mode", "AnyAuthenticatedUser");
        builder.UseSetting("Authorization:PlatformAdministrator:Mode", "Disabled");
        builder.UseSetting("Storage:Provider", "InMemory");

        _configure?.Invoke(builder);

        builder.ConfigureTestServices(services =>
        {
            // Register test scheme under the JWT Bearer name used by policies.
            // Program already registered JwtBearer when Enabled=true; replace
            // its handler registration by re-adding the scheme via authentication
            // options post-configure is fragile, so we add a parallel scheme and
            // rewrite policies to use TestAuthHandler.SchemeName (same constant).
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName + ".Test",
                    _ => { });

            // Override policies to authenticate via the test scheme instead of
            // real JwtBearer (which would attempt OIDC metadata discovery).
            const string testScheme = TestAuthHandler.SchemeName + ".Test";

            services.AddAuthorization(options =>
            {
                options.AddPolicy(PlatformAuthenticationPolicies.JwtBearer, policy =>
                {
                    policy.AddAuthenticationSchemes(testScheme);
                    policy.RequireAuthenticatedUser();
                });

                options.AddPolicy(PlatformAuthenticationPolicies.TenantCreation, policy =>
                {
                    policy.AddAuthenticationSchemes(testScheme);
                    policy.RequireAuthenticatedUser();
                    policy.Requirements.Add(new TenantCreationRequirement());
                });

                options.AddPolicy(PlatformAuthenticationPolicies.TenantMember, policy =>
                {
                    policy.AddAuthenticationSchemes(
                        ApiKeyAuthenticationDefaults.SchemeName,
                        testScheme);
                    policy.RequireAuthenticatedUser();
                    policy.Requirements.Add(new TenantMembershipRequirement());
                });

                options.AddPolicy(PlatformAuthenticationPolicies.PlatformAdministrator, policy =>
                {
                    policy.AddAuthenticationSchemes(testScheme);
                    policy.RequireAuthenticatedUser();
                    policy.Requirements.Add(new PlatformAdministratorRequirement());
                });
            });
        });
    }

    /// <summary>
    /// Creates a client that presents the given OIDC identity via test headers.
    /// </summary>
    public HttpClient CreateAuthenticatedClient(
        string subject,
        string issuer = "https://test-issuer/",
        string? role = null,
        string? claimType = null,
        string? claimValue = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, subject);
        client.DefaultRequestHeaders.Add(TestAuthHandler.IssHeader, issuer);
        if (!string.IsNullOrWhiteSpace(role))
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, role);
        }

        if (!string.IsNullOrWhiteSpace(claimType))
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.ClaimTypeHeader, claimType);
            client.DefaultRequestHeaders.Add(TestAuthHandler.ClaimValueHeader, claimValue ?? string.Empty);
        }

        return client;
    }
}
