// Standalone sample app proving the real, full containerized OTLP flow:
// PlaxionMediator.OpenTelemetry instrumentation -> genuine OTel .NET SDK
// OTLP/HTTP exporter -> containerized Platform.Ingestion (localhost:8081)
// -> Postgres -> aggregation -> Platform.Api -> Dashboard.
//
// Usage:
//   dotnet run -- <apiKey> <ingestionBaseUrl> <serviceName> <environment> <requestCount>

using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using PlaxionMediator.OpenTelemetry;
using PlaxionMediator.Pipeline;

if (args.Length < 2)
{
    Console.WriteLine("Usage: dotnet run -- <apiKey> <ingestionBaseUrl> [serviceName] [environment] [requestCount]");
    return 1;
}

var apiKey = args[0];
var ingestionBaseUrl = args[1];
var serviceName = args.Length > 2 ? args[2] : "otlp-acceptance-sample";
var environmentName = args.Length > 3 ? args[3] : "prod";
var requestCount = args.Length > 4 ? int.Parse(args[4]) : 20;

var plaxionMeterName = PlaxionMediatorMeter.Meter.Name;
_ = PlaxionMediatorMeter.RequestDuration;

var otlpOptions = new OtlpExporterOptions
{
    Protocol = OtlpExportProtocol.HttpProtobuf,
    Endpoint = new Uri(new Uri(ingestionBaseUrl), "v1/metrics"),
    TimeoutMilliseconds = 10_000
};
otlpOptions.HttpClientFactory = () =>
{
    var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", apiKey);
    return httpClient;
};

using var meterProvider = Sdk.CreateMeterProviderBuilder()
    .SetResourceBuilder(ResourceBuilder.CreateDefault()
        .AddService(serviceName)
        .AddAttributes(new KeyValuePair<string, object>[]
        {
            new("deployment.environment", environmentName)
        }))
    .AddMeter(plaxionMeterName)
    .AddOtlpExporter((exporterOptions, readerOptions) =>
    {
        exporterOptions.Protocol = otlpOptions.Protocol;
        exporterOptions.Endpoint = otlpOptions.Endpoint;
        exporterOptions.TimeoutMilliseconds = otlpOptions.TimeoutMilliseconds;
        exporterOptions.HttpClientFactory = otlpOptions.HttpClientFactory;
        readerOptions.TemporalityPreference = MetricReaderTemporalityPreference.Cumulative;
        readerOptions.PeriodicExportingMetricReaderOptions = new PeriodicExportingMetricReaderOptions
        {
            ExportIntervalMilliseconds = 2000
        };
    })
    .Build();

var observer = new OpenTelemetryPipelineObserver();

Console.WriteLine($"Sending {requestCount} PlaxionMediator requests as service '{serviceName}' (env '{environmentName}') to {otlpOptions.Endpoint} ...");

for (var i = 0; i < requestCount; i++)
{
    var isFailure = i % 5 == 0; // ~20% failure rate, similar to real traffic
    var callContext = new PipelineCallContext(typeof(SampleRequest), typeof(SampleResponse), behaviorCount: 0);
    observer.OnStarting(in callContext);
    await Task.Delay(Random.Shared.Next(10, 80));

    if (isFailure)
    {
        observer.OnFaulted(in callContext, new InvalidOperationException("simulated failure"));
    }
    else
    {
        observer.OnCompleted(in callContext);
    }

    await Task.Delay(50);
}

meterProvider.ForceFlush(10_000);
await Task.Delay(3000); // allow the periodic exporter to flush the final batch

Console.WriteLine("Done. Check the Dashboard / Api for the ingested telemetry.");
return 0;

sealed class SampleRequest;
sealed class SampleResponse;
