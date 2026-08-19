using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Platform.Identity;
using Platform.Ingestion.Controllers;
using Xunit;

namespace Platform.Ingestion.Tests;

public class ApiKeysEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiKeysEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_List_Revoke_ApiKeyLifecycle()
    {
        var client = await CreateAuthenticatedClientAsync();

        var createResponse = await client.PostAsJsonAsync("/v1/api-keys", new ApiKeysController.CreateApiKeyRequest("ci-key"));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<ApiKeysController.CreateApiKeyResponse>();
        Assert.NotNull(created);
        Assert.False(string.IsNullOrWhiteSpace(created!.PlaintextKey));
        Assert.StartsWith("pk_", created.PlaintextKey);

        var list = await client.GetFromJsonAsync<List<ApiKeysController.ApiKeyListItem>>("/v1/api-keys");
        Assert.Contains(list!, k => k.Id == created.Id && k.Name == "ci-key" && k.IsActive);

        var revoke = await client.DeleteAsync($"/v1/api-keys/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        // New client with the revoked key must fail auth.
        var revokedClient = _factory.CreateClient();
        revokedClient.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, created.PlaintextKey);
        var unauthorized = await revokedClient.GetAsync("/v1/api-keys");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
    }

    [Fact]
    public async Task Create_WithoutApiKey_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/v1/api-keys", new ApiKeysController.CreateApiKeyRequest("x"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<ApiKeyService>();
        var created = await apiKeyService.CreateAsync(Guid.NewGuid(), "bootstrap");
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, created.PlaintextKey);
        return client;
    }
}
