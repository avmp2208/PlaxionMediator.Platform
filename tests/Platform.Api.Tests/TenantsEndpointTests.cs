using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Platform.Api.Controllers;
using Platform.Identity;
using Xunit;

namespace Platform.Api.Tests;

/// <summary>
/// Covers the anonymous self-service tenant bootstrap endpoint used by the
/// Dashboard to provision a brand-new tenant + first API key.
/// </summary>
public class TenantsEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TenantsEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
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

        // The tenant's service list is reachable directly by TenantId (no key required).
        var servicesResponse = await client.GetAsync($"/v1/tenants/{created.TenantId}/services");
        Assert.Equal(HttpStatusCode.OK, servicesResponse.StatusCode);

        // The returned key still works against key-protected endpoints (e.g. api-key management).
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, created.PlaintextKey);

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
