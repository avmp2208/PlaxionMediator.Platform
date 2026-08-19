using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Identity;
using Platform.Storage;

namespace Platform.Api.Controllers;

// The {tenantId} route segment is NEVER trusted on its own - it must match
// the TenantId resolved from the authenticated X-Api-Key. A mismatch
// resolves to 404 (not 403) to avoid confirming the existence of another
// tenant's id. No API key at all resolves to 401 via [Authorize].
[ApiController]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.SchemeName)]
[Route("v1/tenants/{tenantId:guid}/services")]
public sealed class ServicesController : ControllerBase
{
    private readonly IServiceCatalog _serviceCatalog;

    public ServicesController(IServiceCatalog serviceCatalog)
    {
        _serviceCatalog = serviceCatalog;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ServiceResponse>>> GetServices(Guid tenantId, CancellationToken cancellationToken)
    {
        if (!User.TryGetTenantId(out var authenticatedTenantId) || authenticatedTenantId != tenantId)
        {
            // Do not leak whether another tenant's id exists.
            return NotFound();
        }

        var services = await _serviceCatalog.ListByTenantAsync(authenticatedTenantId, cancellationToken);
        var response = services.Select(s => new ServiceResponse(s.Id, s.Name, s.Environment)).ToList();
        return Ok(response);
    }
}
