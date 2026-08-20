using Microsoft.AspNetCore.Authorization;
using Platform.Identity;
using Platform.Storage;

namespace Platform.Api.Authorization;

/// <summary>
/// Authorizes access to `{tenantId}`-scoped resources.
/// Succeeds when any of:
///   (a) API-key principal whose trusted tenant_id claim matches the route,
///   (b) JWT principal satisfying PlatformAdministrator config (legacy-tenant
///       operational bypass — no membership row required),
///   (c) JWT principal with a <see cref="TenantMembership"/> for (tenantId, iss, sub).
/// </summary>
public sealed class TenantMembershipAuthorizationHandler : AuthorizationHandler<TenantMembershipRequirement>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ITenantMembershipStore _membershipStore;
    private readonly PlatformAdministratorEvaluator _platformAdministrator;

    public TenantMembershipAuthorizationHandler(
        IHttpContextAccessor httpContextAccessor,
        ITenantMembershipStore membershipStore,
        PlatformAdministratorEvaluator platformAdministrator)
    {
        _httpContextAccessor = httpContextAccessor;
        _membershipStore = membershipStore;
        _platformAdministrator = platformAdministrator;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TenantMembershipRequirement requirement)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return;
        }

        if (!httpContext.Request.RouteValues.TryGetValue("tenantId", out var rawTenantId)
            || rawTenantId is null
            || !Guid.TryParse(rawTenantId.ToString(), out var tenantId))
        {
            return;
        }

        var user = context.User;

        // Machine path: trusted tenant_id from a validated API key.
        if (user.TryGetTenantId(out var apiKeyTenantId))
        {
            if (apiKeyTenantId == tenantId)
            {
                context.Succeed(requirement);
            }

            return;
        }

        // Platform operator path: configured admin claim/role bypasses
        // per-tenant membership so legacy tenants remain operable after OIDC
        // is enabled. Cheap claim check before the membership store lookup.
        if (_platformAdministrator.IsPlatformAdministrator(user))
        {
            context.Succeed(requirement);
            return;
        }

        // Human path: OIDC issuer/subject must have a membership row.
        if (!OidcIdentityClaims.TryGetIssuerAndSubject(user, out var issuer, out var subject))
        {
            return;
        }

        var membership = await _membershipStore.GetAsync(
            tenantId,
            issuer,
            subject,
            httpContext.RequestAborted);

        if (membership is not null)
        {
            context.Succeed(requirement);
        }
    }
}
