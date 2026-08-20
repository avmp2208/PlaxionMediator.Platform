using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Platform.Api.Authorization;

/// <summary>
/// Provider-neutral helpers for reading OIDC issuer/subject/email from a JWT principal.
/// Supports both short JWT names (iss/sub/email) and common mapped claim types.
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

    /// <summary>
    /// Resolves and normalizes an email from the principal using the configured
    /// claim type list (first non-empty wins). Normalization is trim + lowercase.
    /// </summary>
    public static bool TryGetEmail(
        ClaimsPrincipal principal,
        IReadOnlyList<string> emailClaimTypes,
        out string normalizedEmail)
    {
        normalizedEmail = string.Empty;
        if (emailClaimTypes.Count == 0)
        {
            return false;
        }

        var types = new string[emailClaimTypes.Count + 1];
        for (var i = 0; i < emailClaimTypes.Count; i++)
        {
            types[i] = emailClaimTypes[i];
        }

        types[^1] = ClaimTypes.Email;

        var raw = FirstClaimValue(principal, types);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        normalizedEmail = NormalizeEmail(raw);
        return normalizedEmail.Length > 0;
    }

    /// <summary>Canonical invitation email form: trim + lowercase invariant.</summary>
    public static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();

    private static string? FirstClaimValue(ClaimsPrincipal principal, params string[] claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            if (string.IsNullOrWhiteSpace(claimType))
            {
                continue;
            }

            var value = principal.FindFirstValue(claimType);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}
