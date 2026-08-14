using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Identity;
using Platform.Storage;

namespace Platform.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.SchemeName)]
[Route("v1/services")]
public sealed class ServicesController : ControllerBase
{
    private readonly IServiceCatalog _serviceCatalog;

    public ServicesController(IServiceCatalog serviceCatalog)
    {
        _serviceCatalog = serviceCatalog;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ServiceResponse>>> GetServices(CancellationToken cancellationToken)
    {
        // TenantId is resolved exclusively from the trusted, authenticated
        // API key context - never from a query string or request body.
        var tenantId = User.GetTenantId();
        var services = await _serviceCatalog.ListByTenantAsync(tenantId, cancellationToken);
        var response = services.Select(s => new ServiceResponse(s.Id, s.Name, s.Environment)).ToList();
        return Ok(response);
    }
}
