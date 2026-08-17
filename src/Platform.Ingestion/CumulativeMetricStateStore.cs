using System.Collections.Concurrent;

namespace Platform.Ingestion;

/// <summary>
/// Tracks the last observed value of cumulative (AGGREGATION_TEMPORALITY_CUMULATIVE) OTel metric
/// series so <see cref="OtlpMetricsMapper"/> can convert repeated/ever-growing cumulative counter
/// reports into per-export deltas instead of persisting the raw running total as a new
/// "execution" every time the exporter fires - which is what made idle periods (no new requests)
/// look like they kept carrying forward the last real activity: the exporter kept re-reporting the
/// same unchanged cumulative total on every periodic export, and each export was being turned into
/// a brand-new MetricSample row regardless of whether anything actually changed.
/// </summary>
/// <remarks>
/// Process-local, in-memory only - matches the rest of the MVP's single-instance assumptions (see
/// docs/mvp-limitations-and-exclusions.md). A restart resets tracked state, which just means the
/// first cumulative report after restart is treated as a fresh baseline (delta = raw value) rather
/// than being compared against a stale reading from before the restart.
/// </remarks>
public sealed class CumulativeMetricStateStore
{
    private readonly ConcurrentDictionary<string, CumulativeCounterState> _counters = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CumulativeHistogramState> _histograms = new(StringComparer.Ordinal);

    /// <summary>
    /// Computes the delta for a cumulative Sum data point and records the new value as the
    /// baseline for the next call. Returns null when the series has never been seen before
    /// (first observation - no baseline to diff against, so it is currently just recorded).
    /// </summary>
    public double? ComputeSumDelta(string seriesKey, double cumulativeValue)
    {
        var previous = _counters.AddOrUpdate(
            seriesKey,
            _ => new CumulativeCounterState(null, cumulativeValue),
            (_, existing) => new CumulativeCounterState(existing.CurrentValue, cumulativeValue));

        return previous.PreviousValue is { } previousValue
            ? ComputeDelta(previousValue, cumulativeValue)
            : null;
    }

    /// <summary>
    /// Computes the (deltaCount, deltaSum) for a cumulative Histogram data point and records the
    /// new totals as the baseline for the next call. Returns null on first observation.
    /// </summary>
    public (long DeltaCount, double DeltaSum)? ComputeHistogramDelta(string seriesKey, long cumulativeCount, double cumulativeSum)
    {
        var previous = _histograms.AddOrUpdate(
            seriesKey,
            _ => new CumulativeHistogramState(null, null, cumulativeCount, cumulativeSum),
            (_, existing) => new CumulativeHistogramState(existing.CurrentCount, existing.CurrentSum, cumulativeCount, cumulativeSum));

        if (previous.PreviousCount is not { } previousCount)
        {
            return null;
        }

        var deltaCount = cumulativeCount - previousCount;
        var deltaSum = cumulativeSum - previous.PreviousSum!.Value;

        // A counter reset (new count/sum lower than what we last saw) is treated as a fresh
        // baseline - the raw values themselves become the "delta" for this observation.
        if (deltaCount < 0)
        {
            deltaCount = cumulativeCount;
            deltaSum = cumulativeSum;
        }

        return (deltaCount, deltaSum);
    }

    private static double ComputeDelta(double previous, double current)
    {
        // A counter reset (current < previous, e.g. instrumented process restarted) is treated as
        // a fresh baseline - the raw current value becomes the delta for this observation.
        return current < previous ? current : current - previous;
    }

    private readonly record struct CumulativeCounterState(double? PreviousValue, double CurrentValue);

    private readonly record struct CumulativeHistogramState(long? PreviousCount, double? PreviousSum, long CurrentCount, double CurrentSum);
}
