using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PlaxionMediator.OpenTelemetry;
using PlaxionMediator.Pipeline;
using Platform.Identity;
using Xunit;

namespace Platform.Ingestion.Tests;

/// <summary>
/// End-to-end proof that a PlaxionMediator + PlaxionMediator.OpenTelemetry
/// instrumented application (using the real, published nuget.org packages
/// - see docs/nuget-reconciliation.md for the corrected finding) produces
/// standard OpenTelemetry metrics that can be mapped and delivered to
/// Platform.Ingestion's POST /v1/ingest and persisted successfully.
///
/// The real PlaxionMediator.OpenTelemetry package instruments the mediator
/// pipeline via <see cref="IPipelineObserver"/> (a compile-time
/// source-generated dispatcher wires this up in a real host app). Here the
/// observer is driven directly with a real <see cref="PipelineCallContext"/>
/// to keep the spike minimal while still exercising the genuine package
/// classes (PlaxionMediatorMeter, PlaxionMediatorActivitySource,
/// OpenTelemetryPipelineObserver) rather than any custom stand-in.
/// </summary>
public class PlaxionMediatorOpenTelemetryIngestionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PlaxionMediatorOpenTelemetryIngestionTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RealPlaxionMediatorOpenTelemetryMetric_IsMappedAndIngestedSuccessfully()
    {
        // Arrange: drive the real OpenTelemetryPipelineObserver from the
        // PlaxionMediator.OpenTelemetry package and capture the resulting
        // standard OTel measurement via a MeterListener - exactly the
        // mechanism a real OTLP exporter would use.
        // Force PlaxionMediatorMeter's static initializer to fully complete
        // before wiring up the listener - otherwise the InstrumentPublished
        // callback re-enters the still-initializing static class while its
        // Histogram is being constructed.
        var meterName = PlaxionMediatorMeter.Meter.Name;
        var requestDurationName = PlaxionMediatorMeter.RequestDuration.Name;

        double? capturedDurationMs = null;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == meterName
                && instrument.Name == requestDurationName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            capturedDurationMs = measurement;
        });
        listener.Start();

        var observer = new OpenTelemetryPipelineObserver();
        var callContext = new PipelineCallContext(typeof(SampleRequest), typeof(SampleResponse), behaviorCount: 0);

        observer.OnStarting(in callContext);
        await Task.Delay(5); // simulate handler work so duration is > 0
        observer.OnCompleted(in callContext);

        listener.Dispose();

        Assert.True(capturedDurationMs.HasValue, "PlaxionMediator.OpenTelemetry did not emit a RequestDuration measurement.");

        // Act: map the captured real OTel measurement into Platform's
        // ingestion transport shape and post it to /v1/ingest.
        var client = await CreateAuthenticatedClientAsync();
        var request = new IngestRequest
        {
            Resource = new IngestResource { ServiceName = "sample-mediator-app", Environment = "test" },
            Metrics = new List<IngestMetricPoint>
            {
                new()
                {
                    MetricName = requestDurationName,
                    Value = capturedDurationMs!.Value,
                    TimeUnixNano = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000
                }
            }
        };

        var response = await client.PostAsJsonAsync("/v1/ingest", request);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<ApiKeyService>();
        var created = await apiKeyService.CreateAsync(Guid.NewGuid(), "otel-spike-key");
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, created.PlaintextKey);
        return client;
    }

    private sealed class SampleRequest
    {
    }

    private sealed class SampleResponse
    {
    }
}
