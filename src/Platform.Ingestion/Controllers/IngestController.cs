using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Identity;
using Platform.Storage;

namespace Platform.Ingestion.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.SchemeName)]
[Route("v1/ingest")]
public sealed class IngestController : ControllerBase
{
    private readonly IMetricSampleStore _store;
    private readonly IServiceCatalog _serviceCatalog;
    private readonly IngestionMetrics _metrics;

    public IngestController(IMetricSampleStore store, IServiceCatalog serviceCatalog, IngestionMetrics metrics)
    {
        _store = store;
        _serviceCatalog = serviceCatalog;
        _metrics = metrics;
    }

    [HttpPost]
    [RequestSizeLimit(1_048_576)] // 1 MiB
    public async Task<IActionResult> Post([FromBody] IngestRequest request, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            // TenantId is resolved exclusively from the trusted, authenticated
            // API key context (Platform.Identity) - never from client input.
            var tenantId = User.GetTenantId();

            IReadOnlyList<MetricSample> samples;
            try
            {
                samples = OtlpToCanonicalMapper.Map(request, tenantId);
            }
            catch (ArgumentException ex)
            {
                _metrics.RejectedTelemetry.Add(1);
                _metrics.IngestRequests.Add(1, new KeyValuePair<string, object?>("outcome", "failure"));
                return BadRequest(new { error = ex.Message });
            }

            foreach (var sample in samples)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _store.AddAsync(sample, cancellationToken);
                await _serviceCatalog.GetOrCreateAsync(sample.TenantId, sample.ServiceName, sample.Environment, cancellationToken);
            }

            _metrics.IngestRequests.Add(1, new KeyValuePair<string, object?>("outcome", "success"));
            return Accepted(new { accepted = samples.Count });
        }
        finally
        {
            _metrics.IngestLatencyMs.Record((DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
        }
    }
}
