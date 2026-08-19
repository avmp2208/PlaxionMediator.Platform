namespace Platform.Aggregation;

/// <summary>
/// Configuration for the raw MetricSample retention/eviction job. Bound from
/// the "Retention" configuration section (e.g. "Retention:RawSampleRetention"),
/// configurable via appsettings/environment in the hosting project
/// (Platform.Api and/or Platform.Ingestion via Platform.Hosting).
/// </summary>
public sealed class RetentionOptions
{
    /// <summary>
    /// How long raw MetricSample rows are kept before eviction. Rollups
    /// already computed from evicted samples (AggregateRollup rows) are
    /// never affected. Defaults to 7 days.
    /// </summary>
    public TimeSpan RawSampleRetention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>How often the retention job checks for expired samples.</summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromMinutes(5);
}
