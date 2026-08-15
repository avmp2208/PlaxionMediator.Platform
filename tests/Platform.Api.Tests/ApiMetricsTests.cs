using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Platform.Identity;
using Platform.Storage;
using Xunit;

namespace Platform.Api.Tests;

/// <summary>
/// Proves Platform.Api's own self-observability instrumentation (ApiMetrics,
/// Meter "Platform.Api") emits measurements via the standard
/// System.Diagnostics.Metrics.MeterListener API on a real query request.
/// </summary>
public class ApiMetricsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiMetricsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetMetrics_RecordsQueryLatencyHistogram()
    {
        using var scope = _factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<ApiKeyService>();
        var serviceCatalog = scope.ServiceProvider.GetRequiredService<IServiceCatalog>();

        var tenantId = Guid.NewGuid();
        var created = await apiKeyService.CreateAsync(tenantId, "metrics-test-key");
        var service = await serviceCatalog.GetOrCreateAsync(tenantId, "svc", "prod");

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, created.PlaintextKey);

        double? latencyMs = null;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == ApiMetrics.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "platform.api.query.latency")
            {
                latencyMs = measurement;
            }
        });
        listener.Start();

        var response = await client.GetAsync($"/v1/tenants/{tenantId}/services/{service.Id}/metrics?range=1h");
        listener.Dispose();

        response.EnsureSuccessStatusCode();
        Assert.True(latencyMs.HasValue, "Expected the API query latency histogram to record a measurement.");
    }

    [Fact]
    public async Task GetMetrics_UnknownService_IncrementsFailuresIsNotRequired_ButLatencyStillRecorded()
    {
        // NotFound (unknown/foreign service id) is a normal outcome, not a
        // failure - only records latency, proving the histogram fires on
        // every request regardless of outcome.
        using var scope = _factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<ApiKeyService>();

        var tenantId = Guid.NewGuid();
        var created = await apiKeyService.CreateAsync(tenantId, "metrics-test-key-2");

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, created.PlaintextKey);

        double? latencyMs = null;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == ApiMetrics.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "platform.api.query.latency")
            {
                latencyMs = measurement;
            }
        });
        listener.Start();

        var response = await client.GetAsync($"/v1/tenants/{tenantId}/services/{Guid.NewGuid()}/metrics?range=1h");
        listener.Dispose();

        Assert.True(latencyMs.HasValue, "Expected the API query latency histogram to record a measurement even for NotFound.");
    }
}
