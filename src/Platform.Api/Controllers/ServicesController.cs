using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Storage;

namespace Platform.Api.Controllers;

// The {tenantId} route segment is NEVER trusted on its own.
// PlatformAuthenticationPolicies.TenantMember verifies either:
//   - an API key whose trusted tenant_id claim matches the route,
//   - a configured platform administrator (legacy-tenant bypass), or
//   - a JWT principal with a TenantMembership row for that tenant.
// Failures map to 404 (not 403) via TenantMembershipAuthorizationResultHandler
// so cross-tenant probes do not confirm another tenant's id. No credentials
// at all still resolves to 401 via [Authorize].
[ApiController]
[Authorize(Policy = PlatformAuthenticationPolicies.TenantMember)]
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
        var services = await _serviceCatalog.ListByTenantAsync(tenantId, cancellationToken);
        var response = services.Select(s => new ServiceResponse(s.Id, s.Name, s.Environment)).ToList();
        return Ok(response);
    }
}
