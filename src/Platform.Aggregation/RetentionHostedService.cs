using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Storage;

namespace Platform.Aggregation;

/// <summary>
/// Scheduled background job that evicts raw MetricSample rows older than the
/// configured retention window (see <see cref="RetentionOptions"/>), across
/// all tenants, via the trusted internal <see cref="IMetricSampleStore.DeleteOlderThanAsync"/>
/// method. Never touches AggregateRollup rows - those are computed and
/// persisted by <see cref="AggregationHostedService"/> before the raw samples
/// they were derived from ever become old enough to be evicted here.
/// </summary>
public sealed class RetentionHostedService : BackgroundService
{
    private readonly IMetricSampleStore _sampleStore;
    private readonly ILogger<RetentionHostedService> _logger;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _retention;

    public RetentionHostedService(
        IMetricSampleStore sampleStore,
        IOptions<RetentionOptions> options,
        ILogger<RetentionHostedService> logger)
    {
        _sampleStore = sampleStore;
        _logger = logger;
        var value = options.Value;
        _interval = value.CheckInterval;
        _retention = value.RawSampleRetention;
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
                _logger.LogError(ex, "Raw sample retention run failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var threshold = DateTimeOffset.UtcNow - _retention;
        await _sampleStore.DeleteOlderThanAsync(threshold, cancellationToken);
        _logger.LogInformation("Raw sample retention run evicted samples older than {Threshold}.", threshold);
    }
}
