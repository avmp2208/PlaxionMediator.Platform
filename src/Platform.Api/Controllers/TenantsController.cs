using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Platform.Api.Authorization;
using Platform.Identity;
using Platform.Storage;

namespace Platform.Api.Controllers;

/// <summary>
/// MVP self-service tenant bootstrap: lets a brand-new user provision a new
/// tenant and its first API key without already having a key (there is no
/// existing "Tenant" storage table - the TenantId here is simply a fresh
/// <see cref="Guid"/>, matching how TenantId is carried implicitly on
/// ApiKey/MetricSample records elsewhere in the platform). Intentionally
/// minimal - no email verification, no billing, no organization hierarchy.
/// When Authentication:Enabled=true, requires JWT Bearer + the TenantCreation
/// policy and records an Owner <see cref="TenantMembership"/> for the caller.
/// When authentication is disabled, anonymous bootstrap remains available.
/// </summary>
[ApiController]
[Route("v1/tenants")]
public sealed class TenantsController : ControllerBase
{
    private const int MaxNameLength = 200;

    private readonly ApiKeyService _apiKeyService;
    private readonly ITenantMembershipStore _membershipStore;
    private readonly IInvitationClaimService _invitationClaimService;

    public TenantsController(
        ApiKeyService apiKeyService,
        ITenantMembershipStore membershipStore,
        IInvitationClaimService invitationClaimService)
    {
        _apiKeyService = apiKeyService;
        _membershipStore = membershipStore;
        _invitationClaimService = invitationClaimService;
    }

    public sealed record CreateTenantRequest(string Name);
    public sealed record CreateTenantResponse(Guid TenantId, string TenantName, Guid ApiKeyId, string ApiKeyName, string PlaintextKey, DateTimeOffset CreatedAt);
    public sealed record TenantListItemResponse(Guid TenantId, string Role, DateTimeOffset CreatedAt);

    /// <summary>
    /// Lists tenants the authenticated OIDC caller belongs to (via
    /// <see cref="ITenantMembershipStore"/>). Before listing, JIT-claims any
    /// Pending invitations matching the caller's configured email claim
    /// (case-insensitive / normalized). Missing email claim skips claim
    /// silently. JWT Bearer only — does not change the POST bootstrap contract.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = PlatformAuthenticationPolicies.JwtBearer)]
    public async Task<ActionResult<IReadOnlyList<TenantListItemResponse>>> List(CancellationToken cancellationToken)
    {
        if (!OidcIdentityClaims.TryGetIssuerAndSubject(User, out var issuer, out var subject))
        {
            return BadRequest(new { error = "Authenticated principal is missing required iss/sub claims." });
        }

        // JIT claim: Pending invitations → Member membership, then list.
        await _invitationClaimService.ClaimPendingAsync(User, cancellationToken);

        var memberships = await _membershipStore.ListByIdentityAsync(issuer, subject, cancellationToken);
        var response = memberships
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => new TenantListItemResponse(m.TenantId, m.Role.ToString(), m.CreatedAt))
            .ToList();

        return Ok(response);
    }

    [HttpPost]
    [Authorize(Policy = PlatformAuthenticationPolicies.TenantCreation)]
    [EnableRateLimiting(RateLimiterPolicies.TenantBootstrap)]
    public async Task<ActionResult<CreateTenantResponse>> Create([FromBody] CreateTenantRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "Name is required." });
        }

        var name = request.Name.Trim();
        if (name.Length > MaxNameLength)
        {
            return BadRequest(new { error = $"Name must be at most {MaxNameLength} characters." });
        }

        string? identityIssuer = null;
        string? identitySubject = null;
        if (User.Identity?.IsAuthenticated == true)
        {
            if (!OidcIdentityClaims.TryGetIssuerAndSubject(User, out identityIssuer, out identitySubject))
            {
                return BadRequest(new { error = "Authenticated principal is missing required iss/sub claims." });
            }
        }

        var tenantId = Guid.NewGuid();
        const string apiKeyName = "Default";
        var createdAt = DateTimeOffset.UtcNow;
        var created = await _apiKeyService.CreateAsync(tenantId, apiKeyName, cancellationToken);

        // Same logical bootstrap operation as the first API key: when the caller
        // is an OIDC user, persist Owner membership so they can access the tenant
        // via JWT on subsequent requests. Storage providers are sequential
        // (matching existing API-key create) rather than a multi-store transaction.
        if (identityIssuer is not null && identitySubject is not null)
        {
            await _membershipStore.AddAsync(
                new TenantMembership
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    IdentityIssuer = identityIssuer,
                    IdentitySubject = identitySubject,
                    Role = TenantMembershipRole.Owner,
                    CreatedAt = createdAt
                },
                cancellationToken);
        }

        // Plaintext returned exactly once here - never persisted or logged.
        var response = new CreateTenantResponse(
            tenantId,
            name,
            created.ApiKeyId,
            apiKeyName,
            created.PlaintextKey,
            createdAt);

        return Created($"/v1/tenants/{tenantId}", response);
    }
}
