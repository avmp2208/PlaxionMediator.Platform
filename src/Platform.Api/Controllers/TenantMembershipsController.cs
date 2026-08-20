using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Api.Authorization;
using Platform.Storage;

namespace Platform.Api.Controllers;

/// <summary>
/// Explicit, auditable membership reconciliation for existing tenants
/// (including pre-OIDC legacy tenants with zero membership rows).
/// Only principals satisfying Authorization:PlatformAdministrator may call
/// this endpoint — there is no automatic first-visitor ownership assignment.
/// Typical upgrade flow: enable Authentication, configure PlatformAdministrator
/// Mode=Role/Claim for a break-glass operator group, POST memberships for each
/// legacy tenant, then set PlatformAdministrator Mode back to Disabled.
/// </summary>
[ApiController]
[Authorize(Policy = PlatformAuthenticationPolicies.PlatformAdministrator)]
[Route("v1/tenants/{tenantId:guid}/memberships")]
public sealed class TenantMembershipsController : ControllerBase
{
    private readonly ITenantMembershipStore _membershipStore;

    public TenantMembershipsController(ITenantMembershipStore membershipStore)
    {
        _membershipStore = membershipStore;
    }

    /// <summary>
    /// Creates a membership for the given OIDC identity on <paramref name="tenantId"/>.
    /// When <see cref="CreateMembershipRequest.IdentityIssuer"/> /
    /// <see cref="CreateMembershipRequest.IdentitySubject"/> are omitted, the
    /// caller's own iss/sub are used (self-grant). Role defaults to Owner.
    /// </summary>
    public sealed record CreateMembershipRequest(
        string? IdentityIssuer,
        string? IdentitySubject,
        TenantMembershipRole? Role);

    public sealed record MembershipResponse(
        Guid Id,
        Guid TenantId,
        string IdentityIssuer,
        string IdentitySubject,
        TenantMembershipRole Role,
        DateTimeOffset CreatedAt);

    [HttpPost]
    public async Task<ActionResult<MembershipResponse>> Create(
        Guid tenantId,
        [FromBody] CreateMembershipRequest? request,
        CancellationToken cancellationToken)
    {
        request ??= new CreateMembershipRequest(null, null, null);

        var issuer = request.IdentityIssuer?.Trim();
        var subject = request.IdentitySubject?.Trim();

        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        {
            if (!OidcIdentityClaims.TryGetIssuerAndSubject(User, out var callerIssuer, out var callerSubject))
            {
                return BadRequest(new
                {
                    error = "identityIssuer and identitySubject are required when the caller principal lacks iss/sub claims."
                });
            }

            if (string.IsNullOrWhiteSpace(issuer))
            {
                issuer = callerIssuer;
            }

            if (string.IsNullOrWhiteSpace(subject))
            {
                subject = callerSubject;
            }
        }

        // Partial body is ambiguous (issuer without subject or vice versa).
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        {
            return BadRequest(new { error = "Both identityIssuer and identitySubject are required." });
        }

        var role = request.Role ?? TenantMembershipRole.Owner;
        if (!Enum.IsDefined(role))
        {
            return BadRequest(new { error = "role must be Owner or Member." });
        }

        var existing = await _membershipStore.GetAsync(tenantId, issuer, subject, cancellationToken);
        if (existing is not null)
        {
            return Conflict(new
            {
                error = "Membership already exists for this tenant and identity.",
                membershipId = existing.Id,
                role = existing.Role.ToString()
            });
        }

        var membership = new TenantMembership
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            IdentityIssuer = issuer,
            IdentitySubject = subject,
            Role = role,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _membershipStore.AddAsync(membership, cancellationToken);

        var response = new MembershipResponse(
            membership.Id,
            membership.TenantId,
            membership.IdentityIssuer,
            membership.IdentitySubject,
            membership.Role,
            membership.CreatedAt);

        return Created($"/v1/tenants/{tenantId}/memberships/{membership.Id}", response);
    }
}
