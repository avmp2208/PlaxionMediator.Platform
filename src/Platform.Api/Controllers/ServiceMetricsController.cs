using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Identity;
using Platform.Storage;

namespace Platform.Api.Controllers;

// The {tenantId} route segment is NEVER trusted on its own - see
// ServicesController for the rationale.
[ApiController]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.SchemeName)]
[Route("v1/tenants/{tenantId:guid}/services/{id:guid}")]
public sealed class ServiceMetricsController : ControllerBase
{
    private readonly IServiceCatalog _serviceCatalog;
    private readonly IAggregateRollupStore _rollupStore;
    private readonly ApiMetrics _metrics;

    public ServiceMetricsController(IServiceCatalog serviceCatalog, IAggregateRollupStore rollupStore, ApiMetrics metrics)
    {
        _serviceCatalog = serviceCatalog;
        _rollupStore = rollupStore;
        _metrics = metrics;
    }

    [HttpGet("metrics")]
    public async Task<ActionResult<IReadOnlyList<MetricRollupResponse>>> GetMetrics(
        Guid tenantId,
        Guid id,
        [FromQuery] string? range,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            if (!User.TryGetTenantId(out var authenticatedTenantId) || authenticatedTenantId != tenantId)
            {
                // Do not leak whether another tenant's id exists.
                return NotFound();
            }

            // Cross-tenant isolation gate: a service id belonging to another
            // tenant must resolve to "not found", never to another tenant's data.
            var service = await _serviceCatalog.GetByIdAsync(authenticatedTenantId, id, cancellationToken);
            if (service is null)
            {
                return NotFound();
            }

            var (from, to) = ParseRange(range);
            var rollups = await _rollupStore.QueryAsync(authenticatedTenantId, id, from, to, cancellationToken);

            var response = rollups.Select(r => new MetricRollupResponse(
                r.BucketStart, r.BucketEnd, r.MetricName, r.Count, r.Min, r.Max, r.Avg, r.P50, r.P95, r.P99, r.RequestType)).ToList();
            return Ok(response);
        }
        catch
        {
            _metrics.QueryFailures.Add(1);
            throw;
        }
        finally
        {
            _metrics.QueryLatencyMs.Record((DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
        }
    }

    [HttpGet("request-types")]
    public async Task<ActionResult<PagedRequestTypeRankingResponse>> GetRequestTypeRanking(
        Guid tenantId,
        Guid id,
        [FromQuery] string? range,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            if (!User.TryGetTenantId(out var authenticatedTenantId) || authenticatedTenantId != tenantId)
            {
                // Do not leak whether another tenant's id exists.
                return NotFound();
            }

            // Cross-tenant isolation gate: a service id belonging to another
            // tenant must resolve to "not found", never to another tenant's data.
            var service = await _serviceCatalog.GetByIdAsync(authenticatedTenantId, id, cancellationToken);
            if (service is null)
            {
                return NotFound();
            }

            if (page < 1)
            {
                page = 1;
            }

            if (pageSize < 1 || pageSize > 100)
            {
                pageSize = 20;
            }

            var (from, to) = ParseRange(range);
            var rollups = await _rollupStore.QueryAsync(authenticatedTenantId, id, from, to, cancellationToken);

            var ranked = BuildRanking(rollups);

            var totalItems = ranked.Count;
            var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);

            var pageItems = ranked
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return Ok(new PagedRequestTypeRankingResponse(pageItems, page, pageSize, totalItems, totalPages));
        }
        catch
        {
            _metrics.QueryFailures.Add(1);
            throw;
        }
        finally
        {
            _metrics.QueryLatencyMs.Record((DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
        }
    }

    /// <summary>
    /// Replicates the frontend's client-side "request types ranking" aggregation
    /// (see metrics-view-model.ts buildRanking) server-side: only rows with a
    /// non-null RequestType participate, executions come from
    /// plaxionmediator.request.count, p95/p99 are a count-weighted average across
    /// buckets from plaxionmediator.request.duration, and failurePercent is the
    /// ratio of plaxionmediator.pipeline_exception.count to executions (0 when
    /// there is no matching exception rollup, not "unknown").
    /// </summary>
    private static IReadOnlyList<RequestTypeRankingResponse> BuildRanking(IReadOnlyList<AggregateRollup> rollups)
    {
        const string requestCountMetric = "plaxionmediator.request.count";
        const string requestDurationMetric = "plaxionmediator.request.duration";
        const string exceptionMetric = "plaxionmediator.pipeline_exception.count";

        var requestCountRows = rollups.Where(r => r.MetricName == requestCountMetric && r.RequestType is not null).ToList();
        var durationRows = rollups.Where(r => r.MetricName == requestDurationMetric && r.RequestType is not null).ToList();
        var exceptionRows = rollups.Where(r => r.MetricName == exceptionMetric && r.RequestType is not null).ToList();

        var requestTypes = requestCountRows.Select(r => r.RequestType!).Distinct().ToList();

        var rows = new List<RequestTypeRankingResponse>();
        foreach (var requestType in requestTypes)
        {
            var countRowsForType = requestCountRows.Where(r => r.RequestType == requestType).ToList();
            var durationRowsForType = durationRows.Where(r => r.RequestType == requestType).ToList();
            var exceptionRowsForType = exceptionRows.Where(r => r.RequestType == requestType).ToList();

            var executionCount = countRowsForType.Sum(r => (long)r.Count);
            var exceptionCount = exceptionRowsForType.Sum(r => (long)r.Count);
            var failurePercent = executionCount == 0 ? 0 : (double)exceptionCount / executionCount * 100;

            var (p95, p99) = WeightedPercentiles(durationRowsForType);

            rows.Add(new RequestTypeRankingResponse(requestType, executionCount, p95, p99, failurePercent));
        }

        return rows
            .OrderByDescending(r => r.ExecutionCount)
            .ThenBy(r => r.RequestType, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Count-weighted average of p95/p99 across duration rollup rows, matching the
    /// frontend's weightedPercentiles function.
    /// </summary>
    private static (double P95, double P99) WeightedPercentiles(IReadOnlyList<AggregateRollup> durationRows)
    {
        var totalCount = durationRows.Sum(r => r.Count);
        if (totalCount == 0)
        {
            return (0, 0);
        }

        double p95 = 0;
        double p99 = 0;
        foreach (var row in durationRows)
        {
            var weight = row.Count / (double)totalCount;
            p95 += row.P95 * weight;
            p99 += row.P99 * weight;
        }

        return (p95, p99);
    }

    /// <summary>
    /// Parses a simple "range" query parameter, e.g. "1h", "24h", "7d".
    /// Defaults to the last 1 hour when absent/unrecognized.
    /// </summary>
    private static (DateTimeOffset From, DateTimeOffset To) ParseRange(string? range)
    {
        var to = DateTimeOffset.UtcNow;
        var window = TimeSpan.FromHours(1);

        if (!string.IsNullOrWhiteSpace(range) && range.Length > 1)
        {
            var unit = range[^1];
            if (int.TryParse(range[..^1], out var amount) && amount > 0)
            {
                window = unit switch
                {
                    'm' => TimeSpan.FromMinutes(amount),
                    'h' => TimeSpan.FromHours(amount),
                    'd' => TimeSpan.FromDays(amount),
                    _ => window
                };
            }
        }

        return (to - window, to);
    }
}
