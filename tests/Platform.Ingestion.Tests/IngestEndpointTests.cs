using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Platform.Identity;
using Xunit;

namespace Platform.Ingestion.Tests;

public class IngestEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public IngestEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<ApiKeyService>();
        var created = await apiKeyService.CreateAsync(Guid.NewGuid(), "test-key");
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, created.PlaintextKey);
        return client;
    }

    [Fact]
    public async Task Post_ValidRequest_ReturnsAccepted()
    {
        var client = await CreateAuthenticatedClientAsync();
        var request = new IngestRequest
        {
            Resource = new IngestResource { ServiceName = "checkout-api", Environment = "prod" },
            Metrics = new List<IngestMetricPoint>
            {
                new() { MetricName = "http.request.duration", Value = 12.5, TimeUnixNano = 1_700_000_000_000_000_000 }
            }
        };

        var response = await client.PostAsJsonAsync("/v1/ingest", request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task Post_InvalidRequest_ReturnsBadRequest()
    {
        var client = await CreateAuthenticatedClientAsync();
        var request = new IngestRequest
        {
            Resource = new IngestResource { ServiceName = "", Environment = "prod" },
            Metrics = new List<IngestMetricPoint>()
        };

        var response = await client.PostAsJsonAsync("/v1/ingest", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_MissingApiKey_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var request = new IngestRequest
        {
            Resource = new IngestResource { ServiceName = "checkout-api", Environment = "prod" },
            Metrics = new List<IngestMetricPoint>
            {
                new() { MetricName = "http.request.duration", Value = 12.5, TimeUnixNano = 1_700_000_000_000_000_000 }
            }
        };

        var response = await client.PostAsJsonAsync("/v1/ingest", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_RevokedApiKey_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<ApiKeyService>();
        var created = await apiKeyService.CreateAsync(Guid.NewGuid(), "revoked-key");
        await apiKeyService.RevokeAsync(created.ApiKeyId);
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, created.PlaintextKey);

        var request = new IngestRequest
        {
            Resource = new IngestResource { ServiceName = "checkout-api", Environment = "prod" },
            Metrics = new List<IngestMetricPoint>
            {
                new() { MetricName = "http.request.duration", Value = 12.5, TimeUnixNano = 1_700_000_000_000_000_000 }
            }
        };

        var response = await client.PostAsJsonAsync("/v1/ingest", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
