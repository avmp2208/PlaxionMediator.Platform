using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Storage;

namespace Platform.Aggregation;

/// <summary>
/// Scheduled (timer-based, non-streaming) background job that periodically
/// rolls up all MetricSample data into AggregateRollup buckets. Reads
/// across all tenants (trusted, system-internal use of
/// IMetricSampleStore.GetAllAsync) but never mixes tenant data when writing
/// rollups - each AggregateRollup carries its own TenantId.
/// </summary>
public sealed class AggregationHostedService : BackgroundService
{
    private readonly IMetricSampleStore _sampleStore;
    private readonly IAggregateRollupStore _rollupStore;
    private readonly IServiceCatalog _serviceCatalog;
    private readonly ILogger<AggregationHostedService> _logger;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _bucketSize;
    private readonly AggregationMetrics _metrics;

    public AggregationHostedService(
        IMetricSampleStore sampleStore,
        IAggregateRollupStore rollupStore,
        IServiceCatalog serviceCatalog,
        ILogger<AggregationHostedService> logger,
        IOptions<AggregationOptions>? options = null,
        AggregationMetrics? metrics = null)
    {
        _sampleStore = sampleStore;
        _rollupStore = rollupStore;
        _serviceCatalog = serviceCatalog;
        _logger = logger;
        var value = options?.Value;
        _interval = value is { Interval: var i } && i > TimeSpan.Zero ? i : TimeSpan.FromMinutes(1);
        _bucketSize = value is { BucketSize: var b } && b > TimeSpan.Zero ? b : TimeSpan.FromMinutes(1);
        _metrics = metrics ?? new AggregationMetrics();
    }

    /// <summary>Test-friendly constructor with explicit intervals.</summary>
    public AggregationHostedService(
        IMetricSampleStore sampleStore,
        IAggregateRollupStore rollupStore,
        IServiceCatalog serviceCatalog,
        ILogger<AggregationHostedService> logger,
        TimeSpan interval,
        TimeSpan bucketSize,
        AggregationMetrics? metrics = null)
    {
        _sampleStore = sampleStore;
        _rollupStore = rollupStore;
        _serviceCatalog = serviceCatalog;
        _logger = logger;
        _interval = interval <= TimeSpan.Zero ? TimeSpan.FromMinutes(1) : interval;
        _bucketSize = bucketSize <= TimeSpan.Zero ? TimeSpan.FromMinutes(1) : bucketSize;
        _metrics = metrics ?? new AggregationMetrics();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Aggregation run failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            var samples = await _sampleStore.GetAllAsync(cancellationToken);
            var rollups = RollupAggregator.Compute(samples, _bucketSize);

            foreach (var rollup in rollups)
            {
                await _serviceCatalog.GetOrCreateAsync(rollup.TenantId, rollup.ServiceName, rollup.Environment, cancellationToken);
            }

            await _rollupStore.AddRangeAsync(rollups, cancellationToken);
            _logger.LogInformation(
                "Aggregation run produced {Count} rollups in {DurationMs}ms.",
                rollups.Count,
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
        }
        catch (Exception) when (RecordFailure())
        {
            throw;
        }
        finally
        {
            _metrics.JobDurationMs.Record((DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
        }
    }

    private bool RecordFailure()
    {
        _metrics.Failures.Add(1);
        return true;
    }
}
