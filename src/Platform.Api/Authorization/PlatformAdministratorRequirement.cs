using Microsoft.AspNetCore.Authorization;

namespace Platform.Api.Authorization;

/// <summary>
/// Marker requirement for the PlatformAdministrator authorization policy.
/// Evaluated by <see cref="PlatformAdministratorAuthorizationHandler"/>.
/// </summary>
public sealed class PlatformAdministratorRequirement : IAuthorizationRequirement;
