using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Platform.Api.Authorization;

/// <summary>
/// Provider-neutral helpers for reading OIDC issuer/subject from a JWT principal.
/// Supports both short JWT names (iss/sub) and common mapped claim types.
/// </summary>
public static class OidcIdentityClaims
{
    /// <summary>
    /// Attempts to resolve OIDC issuer + subject from the principal.
    /// </summary>
    public static bool TryGetIssuerAndSubject(
        ClaimsPrincipal principal,
        out string issuer,
        out string subject)
    {
        issuer = FirstClaimValue(
            principal,
            JwtRegisteredClaimNames.Iss,
            "iss") ?? string.Empty;

        subject = FirstClaimValue(
            principal,
            JwtRegisteredClaimNames.Sub,
            "sub",
            ClaimTypes.NameIdentifier) ?? string.Empty;

        return !string.IsNullOrWhiteSpace(issuer) && !string.IsNullOrWhiteSpace(subject);
    }

    private static string? FirstClaimValue(ClaimsPrincipal principal, params string[] claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            var value = principal.FindFirstValue(claimType);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}
