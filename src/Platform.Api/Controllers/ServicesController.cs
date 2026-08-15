using Microsoft.AspNetCore.Mvc;
using Platform.Storage;

namespace Platform.Api.Controllers;

// NOTE: authentication intentionally removed for the Dashboard "Manage" flow -
// the tenant registry created via POST /v1/tenants is browser-local, and a
// tenant only ever "manages" the tenant it just created/knows the id for.
// Requiring the API key here just to view the service list added friction
// without real protection at this MVP stage (the plaintext key is only ever
// shown once, so re-prompting for it on every "Manage" click was not viable).
// TenantId is therefore trusted directly from the route. Real access control
// (e.g. RBAC or a session-based mechanism) will be revisited as separate
// hardening work, out of scope for now.
[ApiController]
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
