using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Platform.Hosting.Configuration;
using AuthenticationOptions = Platform.Hosting.Configuration.AuthenticationOptions;

namespace Platform.Api.Controllers;

/// <summary>
/// Anonymous runtime configuration for the Angular Dashboard. Public-safe only:
/// no secrets, no DB-backed values, no API keys. Mirrors the shape written to
/// wwwroot/assets/config.json by the container entrypoint.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("v1/platform")]
public sealed class PlatformConfigController : ControllerBase
{
    private readonly AuthenticationOptions _authentication;
    private readonly DashboardOptions _dashboard;

    public PlatformConfigController(
        IOptions<AuthenticationOptions> authentication,
        IOptions<DashboardOptions> dashboard)
    {
        _authentication = authentication.Value;
        _dashboard = dashboard.Value;
    }

    public sealed record AuthenticationConfigResponse(
        bool Enabled,
        string? Authority,
        string? ClientId,
        IReadOnlyList<string> Scopes,
        string RedirectPath,
        string PostLogoutRedirectPath);

    public sealed record PlatformConfigResponse(
        string ApiBaseUrl,
        AuthenticationConfigResponse Authentication);

    [HttpGet("config")]
    [ProducesResponseType(typeof(PlatformConfigResponse), StatusCodes.Status200OK)]
    public ActionResult<PlatformConfigResponse> GetConfig()
    {
        var oidc = _dashboard.Oidc ?? new DashboardOidcOptions();
        var scopes = oidc.Scopes is { Length: > 0 }
            ? oidc.Scopes
            : ["openid", "profile", "email"];

        // Prefer an explicit Dashboard:ApiBaseUrl; otherwise empty string means
        // same-origin (matches the container entrypoint default).
        var apiBaseUrl = _dashboard.ApiBaseUrl ?? string.Empty;

        var authentication = new AuthenticationConfigResponse(
            Enabled: _authentication.Enabled,
            Authority: _authentication.Enabled ? _authentication.Authority : null,
            ClientId: _authentication.Enabled ? oidc.ClientId : null,
            Scopes: scopes,
            RedirectPath: string.IsNullOrWhiteSpace(oidc.RedirectPath)
                ? "/auth/callback"
                : oidc.RedirectPath,
            PostLogoutRedirectPath: string.IsNullOrWhiteSpace(oidc.PostLogoutRedirectPath)
                ? "/"
                : oidc.PostLogoutRedirectPath);

        return Ok(new PlatformConfigResponse(apiBaseUrl, authentication));
    }
}
