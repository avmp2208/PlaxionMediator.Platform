using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Api.Authorization;
using Platform.Storage;

namespace Platform.Api.Controllers;

/// <summary>
/// Multi-tenant email invitations. Owners invite by email across one or more
/// tenants they own; invitees JIT-claim Member membership on GET /v1/tenants.
/// The platform does not send invitation emails and has no separate accept step.
/// IdP/OIDC roles are never written into invitations or memberships.
/// </summary>
[ApiController]
[Authorize(Policy = PlatformAuthenticationPolicies.JwtBearer)]
[Route("v1/invitations")]
public sealed class InvitationsController : ControllerBase
{
    private readonly ITenantInvitationStore _invitationStore;
    private readonly ITenantMembershipStore _membershipStore;
    private readonly ITenantAuthorizationService _tenantAuthorization;

    public InvitationsController(
        ITenantInvitationStore invitationStore,
        ITenantMembershipStore membershipStore,
        ITenantAuthorizationService tenantAuthorization)
    {
        _invitationStore = invitationStore;
        _membershipStore = membershipStore;
        _tenantAuthorization = tenantAuthorization;
    }

    public sealed record CreateInvitationsRequest(string Email, Guid[] TenantIds);

    public sealed record InvitationResponse(
        Guid Id,
        Guid TenantId,
        string Email,
        string Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset? ClaimedAt);

    public sealed record CreateInvitationsResponse(
        IReadOnlyList<InvitationResponse> Invitations,
        IReadOnlyList<Guid> SkippedTenantIds);

    /// <summary>
    /// Creates Pending Member invitations for every listed tenant.
    /// Atomic ownership check: caller must be Owner of every tenantId; if any
    /// tenant is missing, unknown, or not owned, the entire request is rejected
    /// and nothing is written. tenantIds are deduplicated. Email is normalized
    /// (trim + lowercase). Duplicate active Pending (tenant, email) → 409.
    /// If the invitee identity cannot be resolved at create time; already-member
    /// skip happens at JIT claim. When a Pending row already exists for a
    /// tenant+email the whole request fails (unique constraint).
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<CreateInvitationsResponse>> Create(
        [FromBody] CreateInvitationsRequest? request,
        CancellationToken cancellationToken)
    {
        if (!OidcIdentityClaims.TryGetIssuerAndSubject(User, out var issuer, out var subject))
        {
            return BadRequest(new { error = "Authenticated principal is missing required iss/sub claims." });
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(new { error = "email is required." });
        }

        if (request.TenantIds is null || request.TenantIds.Length == 0)
        {
            return BadRequest(new { error = "tenantIds must contain at least one tenant id." });
        }

        var normalizedEmail = OidcIdentityClaims.NormalizeEmail(request.Email);
        if (normalizedEmail.Length == 0 || !normalizedEmail.Contains('@'))
        {
            return BadRequest(new { error = "email must be a non-empty address." });
        }

        // Deduplicate while preserving first-seen order.
        var tenantIds = new List<Guid>();
        var seen = new HashSet<Guid>();
        foreach (var id in request.TenantIds)
        {
            if (id == Guid.Empty)
            {
                return BadRequest(new { error = "tenantIds must not contain empty guids." });
            }

            if (seen.Add(id))
            {
                tenantIds.Add(id);
            }
        }

        // Atomic pre-check: Owner on every tenant before any mutation.
        var unauthorized = new List<Guid>();
        foreach (var tenantId in tenantIds)
        {
            if (!await _tenantAuthorization.HasPermissionAsync(
                    User,
                    tenantId,
                    TenantPermission.ManageInvitations,
                    cancellationToken))
            {
                unauthorized.Add(tenantId);
            }
        }

        if (unauthorized.Count > 0)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "Caller must be Owner of every listed tenant. No invitations were created.",
                unauthorizedTenantIds = unauthorized
            });
        }

        // Skip tenants where a membership already exists for identities we cannot
        // resolve by email at create time. Create-time "already member" skip is
        // applied when a prior Claimed invitation for the same email+tenant exists
        // (same person already joined). Pending duplicates still conflict.
        var skipped = new List<Guid>();
        var toCreate = new List<Guid>();
        foreach (var tenantId in tenantIds)
        {
            var existingPending = await _invitationStore.GetPendingAsync(
                tenantId,
                normalizedEmail,
                cancellationToken);
            if (existingPending is not null)
            {
                return Conflict(new
                {
                    error = "A pending invitation already exists for this tenant and email.",
                    tenantId,
                    invitationId = existingPending.Id
                });
            }

            if (await InviteeAlreadyMemberAsync(tenantId, normalizedEmail, cancellationToken))
            {
                skipped.Add(tenantId);
                continue;
            }

            toCreate.Add(tenantId);
        }

        var createdAt = DateTimeOffset.UtcNow;
        var created = new List<InvitationResponse>();
        foreach (var tenantId in toCreate)
        {
            var invitation = new TenantInvitation
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                NormalizedEmail = normalizedEmail,
                Status = TenantInvitationStatus.Pending,
                CreatedByIssuer = issuer,
                CreatedBySubject = subject,
                CreatedAt = createdAt
            };

            try
            {
                await _invitationStore.AddAsync(invitation, cancellationToken);
            }
            catch (InvalidOperationException)
            {
                // Concurrent duplicate Pending under unique constraint.
                return Conflict(new
                {
                    error = "A pending invitation already exists for this tenant and email.",
                    tenantId
                });
            }

            created.Add(ToResponse(invitation));
        }

        return Created("/v1/invitations", new CreateInvitationsResponse(created, skipped));
    }

    /// <summary>
    /// Lists invitations (pending and claimed/revoked history) for a tenant the
    /// caller owns. Members and unrelated users receive 403.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<InvitationResponse>>> List(
        [FromQuery] Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
        {
            return BadRequest(new { error = "tenantId query parameter is required." });
        }

        if (!await _tenantAuthorization.HasPermissionAsync(
                User,
                tenantId,
                TenantPermission.ManageInvitations,
                cancellationToken))
        {
            return Forbid();
        }

        var invitations = await _invitationStore.ListByTenantAsync(tenantId, cancellationToken);
        return Ok(invitations.Select(ToResponse).ToList());
    }

    /// <summary>
    /// Revokes a Pending invitation. Does not remove any membership already
    /// created by a prior claim. Non-owners receive 403; unknown id → 404.
    /// </summary>
    [HttpPost("{invitationId:guid}/revoke")]
    public async Task<ActionResult<InvitationResponse>> Revoke(
        Guid invitationId,
        CancellationToken cancellationToken)
    {
        var invitation = await _invitationStore.GetByIdAsync(invitationId, cancellationToken);
        if (invitation is null)
        {
            return NotFound();
        }

        if (!await _tenantAuthorization.HasPermissionAsync(
                User,
                invitation.TenantId,
                TenantPermission.ManageInvitations,
                cancellationToken))
        {
            return Forbid();
        }

        if (invitation.Status != TenantInvitationStatus.Pending)
        {
            return Conflict(new
            {
                error = $"Only Pending invitations can be revoked (current status: {invitation.Status}).",
                status = invitation.Status.ToString()
            });
        }

        var revoked = new TenantInvitation
        {
            Id = invitation.Id,
            TenantId = invitation.TenantId,
            NormalizedEmail = invitation.NormalizedEmail,
            Status = TenantInvitationStatus.Revoked,
            CreatedByIssuer = invitation.CreatedByIssuer,
            CreatedBySubject = invitation.CreatedBySubject,
            CreatedAt = invitation.CreatedAt,
            ClaimedAt = invitation.ClaimedAt,
            ClaimedByIssuer = invitation.ClaimedByIssuer,
            ClaimedBySubject = invitation.ClaimedBySubject
        };

        await _invitationStore.UpdateAsync(revoked, cancellationToken);
        return Ok(ToResponse(revoked));
    }

    /// <summary>
    /// Create-time already-member heuristic: if a Claimed invitation for the same
    /// normalized email on this tenant exists and the claimer still has membership,
    /// skip creating another invitation. Memberships themselves are keyed by
    /// iss/sub (not email), so email change after claim does not revoke access.
    /// </summary>
    private async Task<bool> InviteeAlreadyMemberAsync(
        Guid tenantId,
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        var history = await _invitationStore.ListByTenantAsync(tenantId, cancellationToken);
        foreach (var prior in history)
        {
            if (prior.Status != TenantInvitationStatus.Claimed
                || !string.Equals(prior.NormalizedEmail, normalizedEmail, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(prior.ClaimedByIssuer)
                || string.IsNullOrWhiteSpace(prior.ClaimedBySubject))
            {
                continue;
            }

            var membership = await _membershipStore.GetAsync(
                tenantId,
                prior.ClaimedByIssuer,
                prior.ClaimedBySubject,
                cancellationToken);
            if (membership is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static InvitationResponse ToResponse(TenantInvitation invitation) =>
        new(
            invitation.Id,
            invitation.TenantId,
            invitation.NormalizedEmail,
            invitation.Status.ToString(),
            invitation.CreatedAt,
            invitation.ClaimedAt);
}
