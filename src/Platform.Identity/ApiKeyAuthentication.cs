using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Platform.Identity;

public static class ApiKeyAuthenticationDefaults
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    /// <summary>Claim type carrying the trusted TenantId resolved from the API key.</summary>
    public const string TenantIdClaimType = "tenant_id";
}

public sealed class ApiKeyAuthenticationSchemeOptions : AuthenticationSchemeOptions
{
}

/// <summary>
/// ASP.NET Core authentication handler that resolves a trusted TenantId
/// claim from the X-Api-Key header. Never trusts a client-supplied
/// TenantId; the only source of truth is a successfully validated key.
/// </summary>
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationSchemeOptions>
{
    private readonly ApiKeyService _apiKeyService;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ApiKeyService apiKeyService)
        : base(options, logger, encoder)
    {
        _apiKeyService = apiKeyService;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyAuthenticationDefaults.HeaderName, out var headerValues))
        {
            // Authentication middleware runs the default scheme's handler for
            // EVERY request (health probes, anonymous bootstrap, static
            // files...), not just endpoints that actually require auth. For
            // endpoints with no [Authorize]/authorization requirement,
            // returning Fail() here would make the base AuthenticationHandler
            // log a "was not authenticated" Information line on every probe -
            // pure noise, drowning out real auth failures. NoResult() is the
            // documented way to say "this handler has nothing to contribute"
            // without triggering that log or affecting the request (an
            // anonymous endpoint doesn't need a principal at all).
            var endpoint = Context.GetEndpoint();
            var requiresAuth = endpoint?.Metadata.GetMetadata<IAuthorizeData>() is not null;
            if (!requiresAuth)
            {
                return AuthenticateResult.NoResult();
            }

            return AuthenticateResult.Fail("Missing API key header.");
        }

        var providedKey = headerValues.ToString();

        // Never log the raw key value - only whether authentication succeeded.
        var tenantId = await _apiKeyService.ValidateAsync(providedKey, Context.RequestAborted);
        if (tenantId is null)
        {
            return AuthenticateResult.Fail("Invalid or revoked API key.");
        }

        var claims = new[] { new Claim(ApiKeyAuthenticationDefaults.TenantIdClaimType, tenantId.Value.ToString()) };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }
}

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Extracts the trusted TenantId from an authenticated ClaimsPrincipal.
    /// Throws if absent - callers must be behind [Authorize], or must first
    /// check <see cref="TryGetTenantId"/> when the endpoint is not gated by
    /// [Authorize].
    /// </summary>
    public static Guid GetTenantId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ApiKeyAuthenticationDefaults.TenantIdClaimType);
        if (value is null || !Guid.TryParse(value, out var tenantId))
        {
            throw new InvalidOperationException("Authenticated principal is missing a trusted tenant id claim.");
        }

        return tenantId;
    }

    /// <summary>
    /// Attempts to extract the trusted TenantId without throwing. Used by
    /// endpoints that no longer enforce [Authorize] but still need to
    /// derive TenantId only from a successfully validated API key, and must
    /// respond with 401 (rather than an unhandled exception) when no key
    /// was presented or it failed validation.
    /// </summary>
    public static bool TryGetTenantId(this ClaimsPrincipal principal, out Guid tenantId)
    {
        var value = principal.FindFirstValue(ApiKeyAuthenticationDefaults.TenantIdClaimType);
        if (value is null || !Guid.TryParse(value, out tenantId))
        {
            tenantId = Guid.Empty;
            return false;
        }

        return true;
    }
}
