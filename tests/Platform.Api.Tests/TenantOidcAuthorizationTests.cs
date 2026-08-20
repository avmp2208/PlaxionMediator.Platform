using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Platform.Api.Controllers;
using Platform.Identity;
using Platform.Storage;
using Xunit;

namespace Platform.Api.Tests;

/// <summary>
/// Phase 2 OIDC / tenant-membership authorization coverage.
/// </summary>
public sealed class TenantOidcAuthorizationTests
{
    private const string Issuer = "https://test-issuer/";

    [Fact]
    public async Task CreateTenant_AuthenticatedUser_GrantsOwnerMembership()
    {
        await using var factory = new OidcAuthWebApplicationFactory();
        var client = factory.CreateAuthenticatedClient(subject: "user-owner-1", issuer: Issuer);

        var response = await client.PostAsJsonAsync(
            "/v1/tenants",
            new TenantsController.CreateTenantRequest("Oidc Tenant"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<TenantsController.CreateTenantResponse>();
        Assert.NotNull(created);

        using var scope = factory.Services.CreateScope();
        var memberships = scope.ServiceProvider.GetRequiredService<ITenantMembershipStore>();
        var membership = await memberships.GetAsync(created!.TenantId, Issuer, "user-owner-1");

        Assert.NotNull(membership);
        Assert.Equal(TenantMembershipRole.Owner, membership!.Role);
        Assert.Equal(created.TenantId, membership.TenantId);
    }

    [Fact]
    public async Task Creator_CanQueryOwnTenantServices_ViaJwt()
    {
        await using var factory = new OidcAuthWebApplicationFactory();
        var client = factory.CreateAuthenticatedClient(subject: "user-owner-2", issuer: Issuer);

        var createResponse = await client.PostAsJsonAsync(
            "/v1/tenants",
            new TenantsController.CreateTenantRequest("Own Tenant"));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<TenantsController.CreateTenantResponse>();
        Assert.NotNull(created);

        using (var scope = factory.Services.CreateScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<IServiceCatalog>();
            await catalog.GetOrCreateAsync(created!.TenantId, "svc-own", "prod");
        }

        var servicesResponse = await client.GetAsync($"/v1/tenants/{created.TenantId}/services");
        Assert.Equal(HttpStatusCode.OK, servicesResponse.StatusCode);

        var services = await servicesResponse.Content.ReadFromJsonAsync<List<ServiceResponse>>();
        Assert.Contains(services!, s => s.Name == "svc-own");
    }

    [Fact]
    public async Task UnrelatedAuthenticatedUser_CannotQueryAnotherTenant_Returns404()
    {
        await using var factory = new OidcAuthWebApplicationFactory();

        var ownerClient = factory.CreateAuthenticatedClient(subject: "owner-a", issuer: Issuer);
        var createResponse = await ownerClient.PostAsJsonAsync(
            "/v1/tenants",
            new TenantsController.CreateTenantRequest("Tenant A"));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<TenantsController.CreateTenantResponse>();
        Assert.NotNull(created);

        using (var scope = factory.Services.CreateScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<IServiceCatalog>();
            await catalog.GetOrCreateAsync(created!.TenantId, "svc-a", "prod");
        }

        var strangerClient = factory.CreateAuthenticatedClient(subject: "stranger-b", issuer: Issuer);
        var response = await strangerClient.GetAsync($"/v1/tenants/{created.TenantId}/services");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnrelatedAuthenticatedUser_CannotQueryAnotherTenantMetrics_Returns404()
    {
        await using var factory = new OidcAuthWebApplicationFactory();

        var ownerClient = factory.CreateAuthenticatedClient(subject: "owner-metrics", issuer: Issuer);
        var createResponse = await ownerClient.PostAsJsonAsync(
            "/v1/tenants",
            new TenantsController.CreateTenantRequest("Metrics Tenant"));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<TenantsController.CreateTenantResponse>();
        Assert.NotNull(created);

        Service service;
        using (var scope = factory.Services.CreateScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<IServiceCatalog>();
            service = await catalog.GetOrCreateAsync(created!.TenantId, "svc-metrics", "prod");
        }

        var strangerClient = factory.CreateAuthenticatedClient(subject: "stranger-metrics", issuer: Issuer);
        var response = await strangerClient.GetAsync(
            $"/v1/tenants/{created.TenantId}/services/{service.Id}/metrics?range=1h");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedButNotAuthorizedByTenantCreationPolicy_IsDenied()
    {
        await using var factory = new OidcAuthWebApplicationFactory(builder =>
        {
            builder.UseSetting("Authorization:TenantCreation:Mode", "Role");
            builder.UseSetting("Authorization:TenantCreation:RequiredRole", "tenant-admin");
        });

        // Authenticated, but missing the required role.
        var client = factory.CreateAuthenticatedClient(subject: "user-no-role", issuer: Issuer);

        var response = await client.PostAsJsonAsync(
            "/v1/tenants",
            new TenantsController.CreateTenantRequest("Should Fail"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedWithRequiredRole_CanCreateTenant()
    {
        await using var factory = new OidcAuthWebApplicationFactory(builder =>
        {
            builder.UseSetting("Authorization:TenantCreation:Mode", "Role");
            builder.UseSetting("Authorization:TenantCreation:RequiredRole", "tenant-admin");
        });

        var client = factory.CreateAuthenticatedClient(
            subject: "user-with-role",
            issuer: Issuer,
            role: "tenant-admin");

        var response = await client.PostAsJsonAsync(
            "/v1/tenants",
            new TenantsController.CreateTenantRequest("Role Tenant"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedWithRequiredClaim_CanCreateTenant()
    {
        await using var factory = new OidcAuthWebApplicationFactory(builder =>
        {
            builder.UseSetting("Authorization:TenantCreation:Mode", "Claim");
            builder.UseSetting("Authorization:TenantCreation:RequiredClaimType", "permissions");
            builder.UseSetting("Authorization:TenantCreation:RequiredClaimValues:0", "tenants:create");
        });

        var client = factory.CreateAuthenticatedClient(
            subject: "user-with-claim",
            issuer: Issuer,
            claimType: "permissions",
            claimValue: "tenants:create");

        var response = await client.PostAsJsonAsync(
            "/v1/tenants",
            new TenantsController.CreateTenantRequest("Claim Tenant"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousBootstrap_StillWorks_WhenAuthenticationDisabled()
    {
        // Default WebApplicationFactory uses appsettings (Authentication:Enabled=false).
        await using var factory = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/v1/tenants",
            new TenantsController.CreateTenantRequest("Anonymous Dev Tenant"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<TenantsController.CreateTenantResponse>();
        Assert.NotNull(created);
        Assert.False(string.IsNullOrWhiteSpace(created!.PlaintextKey));

        // No membership is recorded for anonymous bootstrap.
        using var scope = factory.Services.CreateScope();
        var memberships = await scope.ServiceProvider
            .GetRequiredService<ITenantMembershipStore>()
            .ListByTenantAsync(created.TenantId);
        Assert.Empty(memberships);

        // Returned API key still works for tenant-scoped resources.
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, created.PlaintextKey);
        var servicesResponse = await client.GetAsync($"/v1/tenants/{created.TenantId}/services");
        Assert.Equal(HttpStatusCode.OK, servicesResponse.StatusCode);
    }

    [Fact]
    public async Task CreateTenant_WhenAuthenticationEnabled_WithoutCredentials_Returns401()
    {
        await using var factory = new OidcAuthWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/v1/tenants",
            new TenantsController.CreateTenantRequest("No Auth"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ApiKey_StillAccessesOwnTenant_WhenOidcEnabled()
    {
        await using var factory = new OidcAuthWebApplicationFactory();
        var ownerClient = factory.CreateAuthenticatedClient(subject: "owner-apikey", issuer: Issuer);

        var createResponse = await ownerClient.PostAsJsonAsync(
            "/v1/tenants",
            new TenantsController.CreateTenantRequest("ApiKey Path"));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<TenantsController.CreateTenantResponse>();
        Assert.NotNull(created);

        var apiKeyClient = factory.CreateClient();
        apiKeyClient.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, created!.PlaintextKey);

        var response = await apiKeyClient.GetAsync($"/v1/tenants/{created.TenantId}/services");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
