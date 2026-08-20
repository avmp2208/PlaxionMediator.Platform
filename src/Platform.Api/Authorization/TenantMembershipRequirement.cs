using Microsoft.AspNetCore.Authorization;

namespace Platform.Api.Authorization;

/// <summary>
/// Marker requirement for tenant-scoped endpoints: caller must be an API key
/// for the route tenant, a configured platform administrator, or a JWT
/// principal with a matching membership row.
/// </summary>
public sealed class TenantMembershipRequirement : IAuthorizationRequirement;
