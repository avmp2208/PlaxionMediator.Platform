using System.Security.Claims;
using Microsoft.Extensions.Options;
using Platform.Hosting.Configuration;

namespace Platform.Api.Authorization;

/// <summary>
/// Shared evaluation of <see cref="PlatformAdministratorOptions"/> against a
/// JWT principal. Used by the named PlatformAdministrator policy and by
/// <see cref="TenantMembershipAuthorizationHandler"/> for cross-tenant bypass
/// of per-tenant membership checks (legacy-tenant reconciliation).
/// </summary>
public sealed class PlatformAdministratorEvaluator
{
    private readonly PlatformAdministratorOptions _options;

    public PlatformAdministratorEvaluator(IOptions<PlatformAdministratorOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// Returns true when the principal is authenticated and satisfies the
    /// configured platform-administrator Mode (Role or Claim). Mode Disabled
    /// always returns false.
    /// </summary>
    public bool IsPlatformAdministrator(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        switch (_options.Mode)
        {
            case PlatformAdministratorMode.Disabled:
                return false;

            case PlatformAdministratorMode.Role:
                return !string.IsNullOrWhiteSpace(_options.RequiredRole)
                       && HasRole(user, _options.RequiredRole.Trim());

            case PlatformAdministratorMode.Claim:
                return HasConfiguredClaim(user);

            default:
                return false;
        }
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
