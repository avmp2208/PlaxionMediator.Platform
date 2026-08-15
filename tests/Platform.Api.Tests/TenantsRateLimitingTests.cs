using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Platform.Api.Controllers;
using Xunit;

namespace Platform.Api.Tests;

/// <summary>
/// Confirms the anonymous POST /v1/tenants bootstrap endpoint is protected
/// by a fixed-window, per-client-IP rate limiter (5 requests/minute) and
/// returns 429 once exceeded. Uses its own <see cref="WebApplicationFactory{TEntryPoint}"/>
/// instance (not shared via a class fixture) so the limiter's in-memory
/// counters are not polluted by calls made from other test classes.
/// </summary>
public class TenantsRateLimitingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TenantsRateLimitingTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_RepeatedlyFromSameClient_EventuallyReturns429()
    {
        // All requests from a single HttpClient hit the TestServer with the
        // same (loopback) RemoteIpAddress, so they share one rate-limit
        // partition - exactly the scenario the limiter is meant to catch.
        var client = _factory.CreateClient();

        var statusCodes = new List<HttpStatusCode>();
        for (var i = 0; i < 10; i++)
        {
            var response = await client.PostAsJsonAsync(
                "/v1/tenants",
                new TenantsController.CreateTenantRequest($"Rate Limit Tenant {i}"));
            statusCodes.Add(response.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statusCodes);
        // The first requests (up to the configured permit limit) must still succeed.
        Assert.Contains(HttpStatusCode.Created, statusCodes);
    }
}
