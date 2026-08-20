using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Platform.Api.Authorization;

/// <summary>
/// Maps failed TenantMember authorization to 404 (not 403) so cross-tenant
/// probes do not confirm whether another tenant id exists — matching the
/// existing controller security posture for API-key mismatches.
/// </summary>
public sealed class TenantMembershipAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden
            && policy.Requirements.OfType<TenantMembershipRequirement>().Any())
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await _defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }
}
