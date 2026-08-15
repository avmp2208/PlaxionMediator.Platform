using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Identity;

namespace Platform.Api.Controllers;

/// <summary>
/// MVP self-service tenant bootstrap: lets a brand-new user provision a new
/// tenant and its first API key without already having a key (there is no
/// existing "Tenant" storage table - the TenantId here is simply a fresh
/// <see cref="Guid"/>, matching how TenantId is carried implicitly on
/// ApiKey/MetricSample records elsewhere in the platform). Intentionally
/// minimal - no email verification, no billing, no organization hierarchy.
/// A production system would likely add stronger anti-abuse controls here
/// (e.g. rate limiting per IP, CAPTCHA), which are out of scope for now.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("v1/tenants")]
public sealed class TenantsController : ControllerBase
{
    private const int MaxNameLength = 200;

    private readonly ApiKeyService _apiKeyService;

    public TenantsController(ApiKeyService apiKeyService)
    {
        _apiKeyService = apiKeyService;
    }

    public sealed record CreateTenantRequest(string Name);
    public sealed record CreateTenantResponse(Guid TenantId, string TenantName, Guid ApiKeyId, string ApiKeyName, string PlaintextKey, DateTimeOffset CreatedAt);

    [HttpPost]
    public async Task<ActionResult<CreateTenantResponse>> Create([FromBody] CreateTenantRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "Name is required." });
        }

        var name = request.Name.Trim();
        if (name.Length > MaxNameLength)
        {
            return BadRequest(new { error = $"Name must be at most {MaxNameLength} characters." });
        }

        var tenantId = Guid.NewGuid();
        const string apiKeyName = "Default";
        var created = await _apiKeyService.CreateAsync(tenantId, apiKeyName, cancellationToken);

        // Plaintext returned exactly once here - never persisted or logged.
        var response = new CreateTenantResponse(
            tenantId,
            name,
            created.ApiKeyId,
            apiKeyName,
            created.PlaintextKey,
            DateTimeOffset.UtcNow);

        return Created($"/v1/tenants/{tenantId}", response);
    }
}
