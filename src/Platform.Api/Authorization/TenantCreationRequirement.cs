using Microsoft.AspNetCore.Authorization;

namespace Platform.Api.Authorization;

/// <summary>
/// Marker requirement for the TenantCreation authorization policy.
/// Evaluated by <see cref="TenantCreationAuthorizationHandler"/>.
/// </summary>
public sealed class TenantCreationRequirement : IAuthorizationRequirement;
