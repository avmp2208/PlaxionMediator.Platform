namespace Platform.Api;

/// <summary>
/// Named rate-limiting policy identifiers used with
/// <c>Microsoft.AspNetCore.RateLimiting</c>, kept in one place so the
/// policy name registered in <c>Program.cs</c> and the name referenced by
/// <c>[EnableRateLimiting]</c> attributes on controllers cannot drift apart.
/// </summary>
public static class RateLimiterPolicies
{
    /// <summary>
    /// Fixed-window, per-client-IP limiter applied to the anonymous
    /// POST /v1/tenants bootstrap endpoint.
    /// </summary>
    public const string TenantBootstrap = "tenant-bootstrap";
}
