using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

    public AggregationHostedService(
        IMetricSampleStore sampleStore,
        IAggregateRollupStore rollupStore,
        IServiceCatalog serviceCatalog,
        ILogger<AggregationHostedService> logger,
        TimeSpan? interval = null,
        TimeSpan? bucketSize = null)
    {
        _sampleStore = sampleStore;
        _rollupStore = rollupStore;
        _serviceCatalog = serviceCatalog;
        _logger = logger;
        _interval = interval ?? TimeSpan.FromMinutes(1);
        _bucketSize = bucketSize ?? TimeSpan.FromMinutes(1);
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
}
