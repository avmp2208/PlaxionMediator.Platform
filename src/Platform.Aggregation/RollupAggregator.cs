using Platform.Storage;

namespace Platform.Aggregation;

/// <summary>
/// Pure, testable aggregation logic: groups MetricSample records into fixed
/// time buckets per (tenant, service, environment, metric) and computes
/// min/max/avg/p50/p95/p99. Invoked by a scheduled job, not a streaming
/// pipeline.
/// </summary>
public static class RollupAggregator
{
    public static IReadOnlyList<AggregateRollup> Compute(IEnumerable<MetricSample> samples, TimeSpan bucketSize)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (bucketSize <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(bucketSize), "Bucket size must be positive.");
        }

        var groups = samples.GroupBy(s => new
        {
            s.TenantId,
            s.ServiceName,
            s.Environment,
            s.MetricName,
            s.RequestType,
            BucketStart = BucketStart(s.Timestamp, bucketSize)
        });

        var rollups = new List<AggregateRollup>();
        foreach (var group in groups)
        {
            var values = group.Select(s => s.Value).OrderBy(v => v).ToList();
            rollups.Add(new AggregateRollup
            {
                Id = Guid.NewGuid(),
                TenantId = group.Key.TenantId,
                ServiceName = group.Key.ServiceName,
                Environment = group.Key.Environment,
                MetricName = group.Key.MetricName,
                RequestType = group.Key.RequestType,
                BucketStart = group.Key.BucketStart,
                BucketEnd = group.Key.BucketStart + bucketSize,
                Count = values.Count,
                Min = values[0],
                Max = values[^1],
                Avg = values.Average(),
                P50 = Percentile(values, 0.50),
                P95 = Percentile(values, 0.95),
                P99 = Percentile(values, 0.99)
            });
        }

        return rollups;
    }

    private static DateTimeOffset BucketStart(DateTimeOffset timestamp, TimeSpan bucketSize)
    {
        var ticks = timestamp.UtcTicks - (timestamp.UtcTicks % bucketSize.Ticks);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    /// <summary>Nearest-rank percentile over a value list that is already sorted ascending.</summary>
    private static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 1)
        {
            return sortedValues[0];
        }

        var rank = (int)Math.Ceiling(percentile * sortedValues.Count) - 1;
        rank = Math.Clamp(rank, 0, sortedValues.Count - 1);
        return sortedValues[rank];
    }
}
