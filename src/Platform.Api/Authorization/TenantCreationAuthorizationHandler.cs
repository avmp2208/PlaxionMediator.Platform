using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Platform.Hosting.Configuration;

namespace Platform.Api.Authorization;

/// <summary>
/// Enforces <see cref="TenantCreationOptions"/> against a JWT principal using
/// only configured role/claim names (no hardcoded IdP-specific claim types).
/// </summary>
public sealed class TenantCreationAuthorizationHandler : AuthorizationHandler<TenantCreationRequirement>
{
    private readonly TenantCreationOptions _options;

    public TenantCreationAuthorizationHandler(IOptions<TenantCreationOptions> options)
    {
        _options = options.Value;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TenantCreationRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Task.CompletedTask;
        }

        switch (_options.Mode)
        {
            case TenantCreationMode.AnyAuthenticatedUser:
                context.Succeed(requirement);
                break;

            case TenantCreationMode.Role:
                if (!string.IsNullOrWhiteSpace(_options.RequiredRole)
                    && HasRole(context.User, _options.RequiredRole.Trim()))
                {
                    context.Succeed(requirement);
                }

                break;

            case TenantCreationMode.Claim:
                if (HasConfiguredClaim(context.User))
                {
                    context.Succeed(requirement);
                }

                break;
        }

        return Task.CompletedTask;
    }

    private bool HasConfiguredClaim(ClaimsPrincipal user)
    {
        if (string.IsNullOrWhiteSpace(_options.RequiredClaimType))
        {
            return false;
        }

        var claimType = _options.RequiredClaimType.Trim();
        var requiredValues = _options.RequiredClaimValues ?? [];

        if (requiredValues.Length == 0)
        {
            return user.Claims.Any(c =>
                string.Equals(c.Type, claimType, StringComparison.Ordinal));
        }

        return requiredValues.Any(value =>
            user.HasClaim(claimType, value));
    }

    /// <summary>
    /// Role check via <see cref="ClaimsPrincipal.IsInRole"/> plus the standard
    /// short JWT role claim names ("role" / "roles") so operators are not forced
    /// onto a single IdP claim mapping.
    /// </summary>
    private static bool HasRole(ClaimsPrincipal user, string role)
    {
        if (user.IsInRole(role))
        {
            return true;
        }

        return user.Claims.Any(c =>
            (string.Equals(c.Type, ClaimTypes.Role, StringComparison.Ordinal)
             || string.Equals(c.Type, "role", StringComparison.OrdinalIgnoreCase)
             || string.Equals(c.Type, "roles", StringComparison.OrdinalIgnoreCase))
            && string.Equals(c.Value, role, StringComparison.Ordinal));
    }
}
