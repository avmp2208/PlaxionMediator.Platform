using Microsoft.AspNetCore.Mvc;
using Platform.Identity;

namespace Platform.Api.Controllers;

/// <summary>
/// Minimal operator surface for API-key lifecycle. Plaintext is returned
/// only once at creation and is never logged. Ported from Platform.Ingestion
/// so the Dashboard can manage API keys for the authenticated tenant
/// directly through Platform.Api.
/// [Authorize] intentionally removed for now - see ServicesController.
/// </summary>
[ApiController]
[Route("v1/api-keys")]
public sealed class ApiKeysController : ControllerBase
{
    private readonly ApiKeyService _apiKeyService;

    public ApiKeysController(ApiKeyService apiKeyService)
    {
        _apiKeyService = apiKeyService;
    }

    public sealed record CreateApiKeyRequest(string Name);
    public sealed record CreateApiKeyResponse(Guid Id, string Name, string PlaintextKey);
    public sealed record ApiKeyListItem(Guid Id, string Name, DateTimeOffset CreatedAt, bool IsActive, DateTimeOffset? RevokedAt);
    public sealed record WhoAmIResponse(Guid TenantId);

    /// <summary>
    /// Resolves the TenantId that an API key belongs to. Used by the
    /// Dashboard's "I already have a key" login fallback, which otherwise
    /// has no way to know which tenant-scoped services URL to navigate to
    /// (viewing services no longer requires the key itself).
    /// </summary>
    [HttpGet("whoami")]
    public IActionResult WhoAmI()
    {
        if (!User.TryGetTenantId(out var tenantId))
        {
            return Unauthorized();
        }

        return Ok(new WhoAmIResponse(tenantId));
    }

    [HttpPost]
    public async Task<ActionResult<CreateApiKeyResponse>> Create([FromBody] CreateApiKeyRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "Name is required." });
        }

        if (!User.TryGetTenantId(out var tenantId))
        {
            return Unauthorized();
        }

        var created = await _apiKeyService.CreateAsync(tenantId, request.Name.Trim(), cancellationToken);
        // Plaintext returned exactly once here.
        return Created($"/v1/api-keys/{created.ApiKeyId}", new CreateApiKeyResponse(created.ApiKeyId, request.Name.Trim(), created.PlaintextKey));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ApiKeyListItem>>> List(CancellationToken cancellationToken)
    {
        if (!User.TryGetTenantId(out var tenantId))
        {
            return Unauthorized();
        }

        var keys = await _apiKeyService.ListAsync(tenantId, cancellationToken);
        var response = keys
            .Select(k => new ApiKeyListItem(k.Id, k.Name, k.CreatedAt, k.IsActive, k.RevokedAt))
            .ToList();
        return Ok(response);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken cancellationToken)
    {
        if (!User.TryGetTenantId(out var tenantId))
        {
            return Unauthorized();
        }

        var keys = await _apiKeyService.ListAsync(tenantId, cancellationToken);
        if (keys.All(k => k.Id != id))
        {
            // Do not leak whether the key exists under another tenant.
            return NotFound();
        }

        await _apiKeyService.RevokeAsync(id, cancellationToken);
        return NoContent();
    }
}
