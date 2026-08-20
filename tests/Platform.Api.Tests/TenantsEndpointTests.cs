using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Platform.Api.Controllers;
using Platform.Identity;
using Xunit;

namespace Platform.Api.Tests;

/// <summary>
/// Covers the self-service tenant bootstrap endpoint and the JWT-backed
/// tenant list used by the Dashboard landing page.
/// </summary>
public class TenantsEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TenantsEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task List_WithoutCredentials_IsDenied()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/v1/tenants");

        // Auth disabled in default test host: JwtBearer policy denies (403).
        // Auth enabled without credentials: challenge yields 401.
        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"Expected 401/403 but got {(int)response.StatusCode} {response.StatusCode}");
    }

    [Fact]
    public async Task List_AuthenticatedUser_ReturnsOnlyOwnMemberships()
    {
        await using var factory = new OidcAuthWebApplicationFactory();
        var owner = factory.CreateAuthenticatedClient(subject: "list-owner", issuer: "https://test-issuer/");
        var stranger = factory.CreateAuthenticatedClient(subject: "list-stranger", issuer: "https://test-issuer/");

        var createA = await owner.PostAsJsonAsync("/v1/tenants", new TenantsController.CreateTenantRequest("Owner Tenant A"));
        var createB = await owner.PostAsJsonAsync("/v1/tenants", new TenantsController.CreateTenantRequest("Owner Tenant B"));
        createA.EnsureSuccessStatusCode();
        createB.EnsureSuccessStatusCode();
        var tenantA = await createA.Content.ReadFromJsonAsync<TenantsController.CreateTenantResponse>();
        var tenantB = await createB.Content.ReadFromJsonAsync<TenantsController.CreateTenantResponse>();
        Assert.NotNull(tenantA);
        Assert.NotNull(tenantB);

        // Unrelated user creates their own tenant — must not appear in owner's list.
        var createOther = await stranger.PostAsJsonAsync("/v1/tenants", new TenantsController.CreateTenantRequest("Stranger Tenant"));
        createOther.EnsureSuccessStatusCode();

        var listResponse = await owner.GetAsync("/v1/tenants");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var tenants = await listResponse.Content.ReadFromJsonAsync<List<TenantsController.TenantListItemResponse>>();
        Assert.NotNull(tenants);
        Assert.Equal(2, tenants!.Count);
        Assert.Contains(tenants, t => t.TenantId == tenantA!.TenantId && t.Role == "Owner");
        Assert.Contains(tenants, t => t.TenantId == tenantB!.TenantId && t.Role == "Owner");
        Assert.DoesNotContain(tenants, t => t.TenantId != tenantA.TenantId && t.TenantId != tenantB.TenantId && t.Role == "Owner" && tenants.Count > 2);

        var strangerList = await stranger.GetFromJsonAsync<List<TenantsController.TenantListItemResponse>>("/v1/tenants");
        Assert.NotNull(strangerList);
        Assert.Single(strangerList);
        Assert.DoesNotContain(strangerList, t => t.TenantId == tenantA.TenantId || t.TenantId == tenantB.TenantId);
    }

    [Fact]
    public async Task Create_WithValidName_Returns201AndWorkingApiKey()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/tenants", new TenantsController.CreateTenantRequest("Acme Corp"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<TenantsController.CreateTenantResponse>();
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created!.TenantId);
        Assert.Equal("Acme Corp", created.TenantName);
        Assert.False(string.IsNullOrWhiteSpace(created.PlaintextKey));
        Assert.StartsWith("pk_", created.PlaintextKey);

        // Without the API key, the tenant's own service list is not reachable.
        var unauthenticatedResponse = await client.GetAsync($"/v1/tenants/{created.TenantId}/services");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedResponse.StatusCode);

        // The returned key works against key-protected endpoints (e.g. api-key management and services).
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, created.PlaintextKey);

        var servicesResponse = await client.GetAsync($"/v1/tenants/{created.TenantId}/services");
        Assert.Equal(HttpStatusCode.OK, servicesResponse.StatusCode);

        var apiKeysResponse = await client.GetAsync("/v1/api-keys");
        Assert.Equal(HttpStatusCode.OK, apiKeysResponse.StatusCode);
        var keys = await apiKeysResponse.Content.ReadFromJsonAsync<List<ApiKeysController.ApiKeyListItem>>();
        Assert.Contains(keys!, k => k.Id == created.ApiKeyId && k.Name == "Default" && k.IsActive);
    }

    [Fact]
    public async Task Create_TwiceInARow_ProducesTwoIsolatedTenants()
    {
        var client = _factory.CreateClient();

        var responseA = await client.PostAsJsonAsync("/v1/tenants", new TenantsController.CreateTenantRequest("Tenant A"));
        var responseB = await client.PostAsJsonAsync("/v1/tenants", new TenantsController.CreateTenantRequest("Tenant B"));

        var createdA = await responseA.Content.ReadFromJsonAsync<TenantsController.CreateTenantResponse>();
        var createdB = await responseB.Content.ReadFromJsonAsync<TenantsController.CreateTenantResponse>();

        Assert.NotEqual(createdA!.TenantId, createdB!.TenantId);
        Assert.NotEqual(createdA.PlaintextKey, createdB.PlaintextKey);

        var clientA = _factory.CreateClient();
        clientA.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, createdA.PlaintextKey);
        var keysA = await clientA.GetFromJsonAsync<List<ApiKeysController.ApiKeyListItem>>("/v1/api-keys");

        var clientB = _factory.CreateClient();
        clientB.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, createdB.PlaintextKey);
        var keysB = await clientB.GetFromJsonAsync<List<ApiKeysController.ApiKeyListItem>>("/v1/api-keys");

        Assert.DoesNotContain(keysA!, k => k.Id == createdB.ApiKeyId);
        Assert.DoesNotContain(keysB!, k => k.Id == createdA.ApiKeyId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_WithEmptyOrWhitespaceName_Returns400(string name)
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/tenants", new TenantsController.CreateTenantRequest(name));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
