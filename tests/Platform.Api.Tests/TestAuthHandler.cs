using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Platform.Api.Tests;

/// <summary>
/// Test double for JWT Bearer: builds a principal from request headers so
/// OIDC authorization paths can be exercised without a real IdP.
/// </summary>
public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = JwtBearerDefaults.AuthenticationScheme;

    public const string SubHeader = "X-Test-Sub";
    public const string IssHeader = "X-Test-Iss";
    public const string RoleHeader = "X-Test-Role";
    public const string ClaimTypeHeader = "X-Test-Claim-Type";
    public const string ClaimValueHeader = "X-Test-Claim-Value";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey(SubHeader))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var subject = Request.Headers[SubHeader].ToString();
        if (string.IsNullOrWhiteSpace(subject))
        {
            return Task.FromResult(AuthenticateResult.Fail("Missing test subject."));
        }

        var issuer = Request.Headers.TryGetValue(IssHeader, out var issValues)
            ? issValues.ToString()
            : "https://test-issuer/";
        if (string.IsNullOrWhiteSpace(issuer))
        {
            issuer = "https://test-issuer/";
        }

        var claims = new List<Claim>
        {
            new("sub", subject),
            new("iss", issuer)
        };

        if (Request.Headers.TryGetValue(RoleHeader, out var roleValues))
        {
            foreach (var role in roleValues.ToString()
                         .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }
        }

        if (Request.Headers.TryGetValue(ClaimTypeHeader, out var claimTypeValues)
            && Request.Headers.TryGetValue(ClaimValueHeader, out var claimValueValues))
        {
            var claimType = claimTypeValues.ToString();
            var claimValue = claimValueValues.ToString();
            if (!string.IsNullOrWhiteSpace(claimType))
            {
                claims.Add(new Claim(claimType, claimValue));
            }
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
