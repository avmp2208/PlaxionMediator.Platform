using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Identity;

namespace Platform.Ingestion.Controllers;

/// <summary>
/// Minimal operator surface for API-key lifecycle. Plaintext is returned
/// only once at creation and is never logged.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.SchemeName)]
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

    [HttpPost]
    public async Task<ActionResult<CreateApiKeyResponse>> Create([FromBody] CreateApiKeyRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "Name is required." });
        }

        var tenantId = User.GetTenantId();
        var created = await _apiKeyService.CreateAsync(tenantId, request.Name.Trim(), cancellationToken);
        // Plaintext returned exactly once here.
        return Created($"/v1/api-keys/{created.ApiKeyId}", new CreateApiKeyResponse(created.ApiKeyId, request.Name.Trim(), created.PlaintextKey));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ApiKeyListItem>>> List(CancellationToken cancellationToken)
    {
        var tenantId = User.GetTenantId();
        var keys = await _apiKeyService.ListAsync(tenantId, cancellationToken);
        var response = keys
            .Select(k => new ApiKeyListItem(k.Id, k.Name, k.CreatedAt, k.IsActive, k.RevokedAt))
            .ToList();
        return Ok(response);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = User.GetTenantId();
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
