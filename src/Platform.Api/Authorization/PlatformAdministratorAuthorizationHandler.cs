using Microsoft.AspNetCore.Authorization;

namespace Platform.Api.Authorization;

/// <summary>
/// Enforces <see cref="Platform.Hosting.Configuration.PlatformAdministratorOptions"/>
/// for the PlatformAdministrator policy (membership reconciliation endpoint).
/// </summary>
public sealed class PlatformAdministratorAuthorizationHandler
    : AuthorizationHandler<PlatformAdministratorRequirement>
{
    private readonly PlatformAdministratorEvaluator _evaluator;

    public PlatformAdministratorAuthorizationHandler(PlatformAdministratorEvaluator evaluator)
    {
        _evaluator = evaluator;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PlatformAdministratorRequirement requirement)
    {
        if (_evaluator.IsPlatformAdministrator(context.User))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
