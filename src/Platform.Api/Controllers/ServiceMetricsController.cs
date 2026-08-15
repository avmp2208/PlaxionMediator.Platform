using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Identity;
using Platform.Storage;

namespace Platform.Api.Controllers;

// The {tenantId} route segment is NEVER trusted on its own - see
// ServicesController for the rationale.
[ApiController]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.SchemeName)]
[Route("v1/tenants/{tenantId:guid}/services/{id:guid}/metrics")]
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

    [HttpGet]
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
                r.BucketStart, r.BucketEnd, r.MetricName, r.Count, r.Min, r.Max, r.Avg, r.P50, r.P95, r.P99)).ToList();
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
