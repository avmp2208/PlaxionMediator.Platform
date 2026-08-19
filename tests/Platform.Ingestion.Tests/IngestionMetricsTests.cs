using System.Diagnostics.Metrics;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Platform.Identity;
using Xunit;

namespace Platform.Ingestion.Tests;

/// <summary>
/// Proves Platform.Ingestion's own self-observability instrumentation
/// (IngestionMetrics, Meter "Platform.Ingestion") emits measurements via
/// the standard System.Diagnostics.Metrics.MeterListener API - the same
/// mechanism a real OTLP exporter (or PlaxionMediatorOpenTelemetryIngestionTests'
/// listener against the real PlaxionMediator.OpenTelemetry meter) would use.
/// This is intentionally a separate Meter from PlaxionMediator's own -
/// Platform's self-observability telemetry is never fed back into
/// POST /v1/metrics.
/// </summary>
public class IngestionMetricsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public IngestionMetricsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ValidIngestRequest_IncrementsSuccessCounterAndRecordsLatency()
    {
        long successCount = 0;
        double? latencyMs = null;

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == IngestionMetrics.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "platform.ingestion.requests")
            {
                foreach (var tag in tags)
                {
                    if (tag.Key == "outcome" && Equals(tag.Value, "success"))
                    {
                        successCount += measurement;
                    }
                }
            }
        });
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "platform.ingestion.latency")
            {
                latencyMs = measurement;
            }
        });
        listener.Start();

        var client = await CreateAuthenticatedClientAsync();
        var request = new IngestRequest
        {
            Resource = new IngestResource { ServiceName = "svc", Environment = "test" },
            Metrics = new List<IngestMetricPoint>
            {
                new()
                {
                    MetricName = "cpu.usage",
                    Value = 1.0,
                    TimeUnixNano = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000
                }
            }
        };

        var response = await client.PostAsJsonAsync("/v1/ingest", request);
        listener.Dispose();

        response.EnsureSuccessStatusCode();
        Assert.True(successCount >= 1, "Expected the ingestion success counter to be incremented.");
        Assert.True(latencyMs.HasValue, "Expected the ingestion latency histogram to record a measurement.");
    }

    [Fact]
    public async Task InvalidIngestRequest_IncrementsRejectedCounter()
    {
        long rejectedCount = 0;

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == IngestionMetrics.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "platform.ingestion.rejected")
            {
                rejectedCount += measurement;
            }
        });
        listener.Start();

        var client = await CreateAuthenticatedClientAsync();
        // No Resource.ServiceName -> invalid telemetry, rejected by the mapper.
        var request = new IngestRequest
        {
            Resource = new IngestResource { ServiceName = "", Environment = "test" },
            Metrics = new List<IngestMetricPoint>
            {
                new()
                {
                    MetricName = "cpu.usage",
                    Value = 1.0,
                    TimeUnixNano = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000
                }
            }
        };

        await client.PostAsJsonAsync("/v1/ingest", request);
        listener.Dispose();

        Assert.True(rejectedCount >= 1, "Expected the rejected-telemetry counter to be incremented.");
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<ApiKeyService>();
        var created = await apiKeyService.CreateAsync(Guid.NewGuid(), "ingestion-metrics-test-key");
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, created.PlaintextKey);
        return client;
    }
}
