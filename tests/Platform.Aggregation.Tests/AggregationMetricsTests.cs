using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.Storage;
using Xunit;

namespace Platform.Aggregation.Tests;

/// <summary>
/// Proves Platform.Aggregation's self-observability instrumentation
/// (AggregationMetrics, Meter "Platform.Aggregation") emits measurements via
/// the standard System.Diagnostics.Metrics.MeterListener API when a real
/// aggregation run completes.
/// </summary>
public class AggregationMetricsTests
{
    [Fact]
    public async Task RunOnceAsync_RecordsJobDurationHistogram()
    {
        var sampleStore = new InMemoryMetricSampleStore();
        var rollupStore = new InMemoryAggregateRollupStore();
        var serviceCatalog = new InMemoryServiceCatalog();
        var metrics = new AggregationMetrics();

        await sampleStore.AddAsync(new MetricSample
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ServiceName = "checkout-api",
            Environment = "prod",
            MetricName = "http.request.duration",
            Value = 10,
            Timestamp = DateTimeOffset.UtcNow
        });

        double? durationMs = null;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == AggregationMetrics.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "platform.aggregation.job.duration")
            {
                durationMs = measurement;
            }
        });
        listener.Start();

        var hostedService = new AggregationHostedService(
            sampleStore, rollupStore, serviceCatalog, NullLogger<AggregationHostedService>.Instance, metrics: metrics);
        await hostedService.RunOnceAsync(CancellationToken.None);

        listener.Dispose();

        Assert.True(durationMs.HasValue, "Expected the aggregation job duration histogram to record a measurement.");
    }

    [Fact]
    public async Task RunOnceAsync_IncrementsFailureCounter_WhenSampleStoreThrows()
    {
        var sampleStore = new ThrowingMetricSampleStore();
        var rollupStore = new InMemoryAggregateRollupStore();
        var serviceCatalog = new InMemoryServiceCatalog();
        var metrics = new AggregationMetrics();

        long failureCount = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == AggregationMetrics.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "platform.aggregation.failures")
            {
                failureCount += measurement;
            }
        });
        listener.Start();

        var hostedService = new AggregationHostedService(
            sampleStore, rollupStore, serviceCatalog, NullLogger<AggregationHostedService>.Instance, metrics: metrics);

        await Assert.ThrowsAsync<InvalidOperationException>(() => hostedService.RunOnceAsync(CancellationToken.None));

        listener.Dispose();

        Assert.True(failureCount >= 1, "Expected the aggregation failure counter to be incremented.");
    }

    private sealed class ThrowingMetricSampleStore : IMetricSampleStore
    {
        public Task AddAsync(MetricSample sample, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MetricSample>> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MetricSample>> QueryAsync(
            Guid tenantId, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MetricSample>> GetAllAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated storage failure.");

        public Task DeleteOlderThanAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
