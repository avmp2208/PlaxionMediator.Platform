namespace Platform.Aggregation;

/// <summary>
/// Configuration for the aggregation background job. Bound from the
/// "Aggregation" configuration section (e.g. "Aggregation:Interval").
/// </summary>
public sealed class AggregationOptions
{
    public const string SectionName = "Aggregation";

    /// <summary>How often the aggregation job runs.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Rollup bucket size.</summary>
    public TimeSpan BucketSize { get; set; } = TimeSpan.FromMinutes(1);
}
