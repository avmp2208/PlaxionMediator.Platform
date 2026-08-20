using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Api.Authorization;
using Platform.Identity;

namespace Platform.Api.Controllers;

/// <summary>
/// Minimal operator surface for API-key lifecycle. Plaintext is returned
/// only once at creation and is never logged. Ported from Platform.Ingestion
/// so the Dashboard can manage API keys for the authenticated tenant
/// directly through Platform.Api.
/// Machine path: X-Api-Key resolves tenant_id (unchanged).
/// Human JWT path: tenantId query requires Owner
/// (<see cref="TenantPermission.ManageApiKeys"/>) via
/// <see cref="ITenantAuthorizationService"/>.
/// </summary>
[ApiController]
[Authorize(Policy = PlatformAuthenticationPolicies.ApiKeyManagement)]
[Route("v1/api-keys")]
public sealed class ApiKeysController : ControllerBase
{
    private readonly ApiKeyService _apiKeyService;
    private readonly ITenantAuthorizationService _tenantAuthorization;

    public ApiKeysController(
        ApiKeyService apiKeyService,
        ITenantAuthorizationService tenantAuthorization)
    {
        _apiKeyService = apiKeyService;
        _tenantAuthorization = tenantAuthorization;
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
    public async Task<ActionResult<CreateApiKeyResponse>> Create(
        [FromBody] CreateApiKeyRequest request,
        [FromQuery] Guid? tenantId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "Name is required." });
        }

        var resolved = await ResolveTenantForApiKeyManagementAsync(tenantId, cancellationToken);
        if (resolved.Result is not null)
        {
            return resolved.Result;
        }

        var created = await _apiKeyService.CreateAsync(resolved.TenantId, request.Name.Trim(), cancellationToken);
        // Plaintext returned exactly once here.
        return Created($"/v1/api-keys/{created.ApiKeyId}", new CreateApiKeyResponse(created.ApiKeyId, request.Name.Trim(), created.PlaintextKey));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ApiKeyListItem>>> List(
        [FromQuery] Guid? tenantId,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveTenantForApiKeyManagementAsync(tenantId, cancellationToken);
        if (resolved.Result is not null)
        {
            return resolved.Result;
        }

        var keys = await _apiKeyService.ListAsync(resolved.TenantId, cancellationToken);
        var response = keys
            .Select(k => new ApiKeyListItem(k.Id, k.Name, k.CreatedAt, k.IsActive, k.RevokedAt))
            .ToList();
        return Ok(response);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(
        Guid id,
        [FromQuery] Guid? tenantId,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveTenantForApiKeyManagementAsync(tenantId, cancellationToken);
        if (resolved.Result is not null)
        {
            return resolved.Result;
        }

        var keys = await _apiKeyService.ListAsync(resolved.TenantId, cancellationToken);
        if (keys.All(k => k.Id != id))
        {
            // Do not leak whether the key exists under another tenant.
            return NotFound();
        }

        await _apiKeyService.RevokeAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// API-key machine principals keep working without membership checks.
    /// JWT humans must pass tenantId and hold ManageApiKeys (Owner).
    /// </summary>
    private async Task<(Guid TenantId, ActionResult? Result)> ResolveTenantForApiKeyManagementAsync(
        Guid? tenantIdQuery,
        CancellationToken cancellationToken)
    {
        if (User.TryGetTenantId(out var apiKeyTenantId))
        {
            // Optional consistency check when both API key and tenantId query are present.
            if (tenantIdQuery is { } q && q != Guid.Empty && q != apiKeyTenantId)
            {
                return (Guid.Empty, Unauthorized());
            }

            return (apiKeyTenantId, null);
        }

        if (tenantIdQuery is null || tenantIdQuery == Guid.Empty)
        {
            return (Guid.Empty, Unauthorized());
        }

        if (!await _tenantAuthorization.HasPermissionAsync(
                User,
                tenantIdQuery.Value,
                TenantPermission.ManageApiKeys,
                cancellationToken))
        {
            return (Guid.Empty, Forbid());
        }

        return (tenantIdQuery.Value, null);
    }
}
