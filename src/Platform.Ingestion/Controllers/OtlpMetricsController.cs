using System.IO.Compression;
using Google.Protobuf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using Platform.Identity;
using Platform.Storage;

namespace Platform.Ingestion.Controllers;

/// <summary>
/// Standard OTLP/HTTP protobuf metrics receiver (POST /v1/metrics).
/// Authenticates via X-Api-Key; TenantId is derived only from the key.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.SchemeName)]
[Route("v1/metrics")]
public sealed class OtlpMetricsController : ControllerBase
{
    private readonly IMetricSampleStore _store;
    private readonly IServiceCatalog _serviceCatalog;
    private readonly IngestionMetrics _metrics;

    public OtlpMetricsController(IMetricSampleStore store, IServiceCatalog serviceCatalog, IngestionMetrics metrics)
    {
        _store = store;
        _serviceCatalog = serviceCatalog;
        _metrics = metrics;
    }

    [HttpPost]
    [RequestSizeLimit(1_048_576)] // 1 MiB — keep OTLP batches bounded
    // Intentionally no [Consumes]: OTLP exporters vary Content-Type slightly
    // (application/x-protobuf, application/protobuf, optional charset).
    public async Task<IActionResult> Post(CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            var tenantId = User.GetTenantId();

            using var raw = new MemoryStream();
            await Request.Body.CopyToAsync(raw, cancellationToken);
            raw.Position = 0;

            ExportMetricsServiceRequest request;
            try
            {
                var encoding = Request.Headers.ContentEncoding.ToString();
                if (encoding.Contains("gzip", StringComparison.OrdinalIgnoreCase))
                {
                    await using var gzip = new GZipStream(raw, CompressionMode.Decompress, leaveOpen: true);
                    request = ExportMetricsServiceRequest.Parser.ParseFrom(gzip);
                }
                else
                {
                    request = ExportMetricsServiceRequest.Parser.ParseFrom(raw);
                }
            }
            catch (InvalidProtocolBufferException)
            {
                _metrics.RejectedTelemetry.Add(1);
                _metrics.IngestRequests.Add(1, new KeyValuePair<string, object?>("outcome", "failure"));
                return BadRequest(new { error = "Invalid OTLP protobuf payload." });
            }
            catch (InvalidDataException)
            {
                _metrics.RejectedTelemetry.Add(1);
                _metrics.IngestRequests.Add(1, new KeyValuePair<string, object?>("outcome", "failure"));
                return BadRequest(new { error = "Invalid gzip content-encoding on OTLP payload." });
            }

            IReadOnlyList<MetricSample> samples;
            try
            {
                samples = OtlpMetricsMapper.Map(request, tenantId);
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

            // OTLP/HTTP full success: 200 OK with ExportMetricsServiceResponse body.
            var response = new ExportMetricsServiceResponse();
            var bytes = response.ToByteArray();
            return File(bytes, "application/x-protobuf");
        }
        finally
        {
            _metrics.IngestLatencyMs.Record((DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
        }
    }
}
