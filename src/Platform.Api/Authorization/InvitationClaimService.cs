using System.Security.Claims;
using Microsoft.Extensions.Options;
using Platform.Hosting.Configuration;
using Platform.Storage;

namespace Platform.Api.Authorization;

/// <summary>
/// JIT-claims Pending invitations for the authenticated principal's email.
/// Invoked from GET /v1/tenants before listing memberships. Idempotent under
/// concurrent callers (duplicate membership inserts are ignored).
/// </summary>
public interface IInvitationClaimService
{
    Task ClaimPendingAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);
}

public sealed class InvitationClaimService : IInvitationClaimService
{
    private readonly ITenantInvitationStore _invitationStore;
    private readonly ITenantMembershipStore _membershipStore;
    private readonly InvitationOptions _options;

    public InvitationClaimService(
        ITenantInvitationStore invitationStore,
        ITenantMembershipStore membershipStore,
        IOptions<InvitationOptions> options)
    {
        _invitationStore = invitationStore;
        _membershipStore = membershipStore;
        _options = options.Value;
    }

    public async Task ClaimPendingAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        if (!OidcIdentityClaims.TryGetIssuerAndSubject(user, out var issuer, out var subject))
        {
            return;
        }

        if (!OidcIdentityClaims.TryGetEmail(user, _options.EmailClaimTypes, out var normalizedEmail))
        {
            // No email claim: skip silently (do not throw, do not create anything).
            return;
        }

        var pending = await _invitationStore.ListPendingByNormalizedEmailAsync(
            normalizedEmail,
            cancellationToken);

        if (pending.Count == 0)
        {
            return;
        }

        var claimedAt = DateTimeOffset.UtcNow;
        foreach (var invitation in pending)
        {
            await ClaimOneAsync(invitation, issuer, subject, claimedAt, cancellationToken);
        }
    }

    private async Task ClaimOneAsync(
        TenantInvitation invitation,
        string issuer,
        string subject,
        DateTimeOffset claimedAt,
        CancellationToken cancellationToken)
    {
        // Re-read to avoid racing a revoke between list and claim.
        var current = await _invitationStore.GetByIdAsync(invitation.Id, cancellationToken);
        if (current is null || current.Status != TenantInvitationStatus.Pending)
        {
            return;
        }

        var existing = await _membershipStore.GetAsync(
            current.TenantId,
            issuer,
            subject,
            cancellationToken);

        if (existing is null)
        {
            try
            {
                await _membershipStore.AddAsync(
                    new TenantMembership
                    {
                        Id = Guid.NewGuid(),
                        TenantId = current.TenantId,
                        IdentityIssuer = issuer,
                        IdentitySubject = subject,
                        Role = TenantMembershipRole.Member,
                        CreatedAt = claimedAt
                    },
                    cancellationToken);
            }
            catch (InvalidOperationException)
            {
                // Concurrent claim already inserted membership — continue to mark claimed.
            }
        }

        // Mark claimed even when membership already existed (invitee was already
        // a Member/Owner via another path). Claimed/Revoked are never reclaimed.
        var stillPending = await _invitationStore.GetByIdAsync(current.Id, cancellationToken);
        if (stillPending is null || stillPending.Status != TenantInvitationStatus.Pending)
        {
            return;
        }

        await _invitationStore.UpdateAsync(
            new TenantInvitation
            {
                Id = stillPending.Id,
                TenantId = stillPending.TenantId,
                NormalizedEmail = stillPending.NormalizedEmail,
                Status = TenantInvitationStatus.Claimed,
                CreatedByIssuer = stillPending.CreatedByIssuer,
                CreatedBySubject = stillPending.CreatedBySubject,
                CreatedAt = stillPending.CreatedAt,
                ClaimedAt = claimedAt,
                ClaimedByIssuer = issuer,
                ClaimedBySubject = subject
            },
            cancellationToken);
    }
}
