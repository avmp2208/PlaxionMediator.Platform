using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Platform.Api.Controllers;
using Platform.Storage;
using Xunit;

namespace Platform.Api.Tests;

/// <summary>
/// Multi-tenant invitations, JIT claim, Owner/Member separation, and related edge cases.
/// </summary>
public sealed class TenantInvitationsTests
{
    private const string Issuer = "https://test-issuer/";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task CreateInvitation_Owner_CreatesPendingMemberInvite()
    {
        await using var factory = new OidcAuthWebApplicationFactory();
        var owner = factory.CreateAuthenticatedClient("owner-1", Issuer, email: "owner@example.com");
        var tenantId = await CreateTenantAsync(owner, "Invite Tenant");

        var response = await owner.PostAsJsonAsync(
            "/v1/invitations",
            new InvitationsController.CreateInvitationsRequest("Invitee@Example.COM ", [tenantId]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<InvitationsController.CreateInvitationsResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Single(body!.Invitations);
        Assert.Empty(body.SkippedTenantIds);
        Assert.Equal(tenantId, body.Invitations[0].TenantId);
        Assert.Equal("invitee@example.com", body.Invitations[0].Email);
        Assert.Equal("Pending", body.Invitations[0].Status);
    }

    [Fact]
    public async Task CreateInvitation_DeduplicatesTenantIds()
    {
        await using var factory = new OidcAuthWebApplicationFactory();
        var owner = factory.CreateAuthenticatedClient("owner-dedup", Issuer);
        var tenantId = await CreateTenantAsync(owner, "Dedup Tenant");

        var response = await owner.PostAsJsonAsync(
            "/v1/invitations",
            new InvitationsController.CreateInvitationsRequest(
                "user@example.com",
                [tenantId, tenantId, tenantId]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<InvitationsController.CreateInvitationsResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Single(body!.Invitations);
    }

    [Fact]
    public async Task CreateInvitation_MultiTenant_AtomicAllOrNothing_PartialOwnership()
    {
        await using var factory = new OidcAuthWebApplicationFactory();
        var ownerA = factory.CreateAuthenticatedClient("owner-a", Issuer);
        var ownerB = factory.CreateAuthenticatedClient("owner-b", Issuer);
        var tenantA = await CreateTenantAsync(ownerA, "A");
        var tenantB = await CreateTenantAsync(ownerB, "B");

        var response = await ownerA.PostAsJsonAsync(
            "/v1/invitations",
            new InvitationsController.CreateInvitationsRequest(
                "multi@example.com",
                [tenantA, tenantB]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ITenantInvitationStore>();
        var forA = await store.ListByTenantAsync(tenantA);
        var forB = await store.ListByTenantAsync(tenantB);
        Assert.Empty(forA);
        Assert.Empty(forB);
    }

    [Fact]
    public async Task CreateInvitation_UnknownTenantId_RejectsAll()
    {
        await using var factory = new OidcAuthWebApplicationFactory();
        var owner = factory.CreateAuthenticatedClient("owner-unknown", Issuer);
        var owned = await CreateTenantAsync(owner, "Owned");
        var unknown = Guid.NewGuid();

        var response = await owner.PostAsJsonAsync(
            "/v1/invitations",
            new InvitationsController.CreateInvitationsRequest(
                "x@example.com",
                [owned, unknown]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ITenantInvitationStore>();
        Assert.Empty(await store.ListByTenantAsync(owned));
    }

    [Fact]
    public async Task CreateInvitation_DuplicatePending_Returns409()
    {
        await using var factory = new OidcAuthWebApplicationFactory();
        var owner = factory.CreateAuthenticatedClient("owner-dup", Issuer);
        var tenantId = await CreateTenantAsync(owner, "Dup Tenant");

        var first = await owner.PostAsJsonAsync(
            "/v1/invitations",
            new InvitationsController.CreateInvitationsRequest("dup@example.com", [tenantId]));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await owner.PostAsJsonAsync(
            "/v1/invitations",
            new InvitationsController.CreateInvitationsRequest("DUP@example.com", [tenantId]));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task CreateInvitation_MemberCannotInvite_Returns403()
    {
        await using var factory = new OidcAuthWebApplicationFactory();
        var owner = factory.CreateAuthenticatedClient("owner-member-forbid", Issuer);
        var tenantId = await CreateTenantAsync(owner, "Member Forbid");

        // Seed Member membership directly.
        using (var scope = factory.Services.CreateScope())
        {
            var memberships = scope.ServiceProvider.GetRequiredService<ITenantMembershipStore>();
            await memberships.AddAsync(new TenantMembership
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                IdentityIssuer = Issuer,
                IdentitySubject = "just-a-member",
                Role = TenantMembershipRole.Member,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        var member = factory.CreateAuthenticatedClient("just-a-member", Issuer);
        var response = await member.PostAsJsonAsync(
            "/v1/invitations",
            new InvitationsController.CreateInvitationsRequest("other@example.com", [tenantId]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateInvitation_UnrelatedUser_Returns403()
    {
        await using var factory = new OidcAuthWebApplicationFactory();
        var owner = factory.CreateAuthenticatedClient("owner-unrelated", Issuer);
        var tenantId = await CreateTenantAsync(owner, "Unrelated");
        var stranger = factory.CreateAuthenticatedClient("stranger", Issuer);

        var response = await stranger.PostAsJsonAsync(
            "/v1/invitations",
            new InvitationsController.CreateInvitationsRequest("x@example.com", [tenantId]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task JitClaim_FirstLogin_CreatesMemberAndListsTenant()
    {
        await using var factory = new OidcAuthWebApplicationFactory();
        var owner = factory.CreateAuthenticatedClient("owner-jit", Issuer);
        var tenantId = await CreateTenantAsync(owner, "JIT Tenant");

        var invite = await owner.PostAsJsonAsync(
            "/v1/invitations",
            new InvitationsController.CreateInvitationsRequest("New.User@Example.com", [tenantId]));
        invite.EnsureSuccessStatusCode();

        var invitee = factory.CreateAuthenticatedClient(
            "invitee-sub-1",
            Issuer,
            email: "new.user@example.com");

        var list = await invitee.GetFromJsonAsync<List<TenantsController.TenantListItemResponse>>("/v1/tenants");
        Assert.NotNull(list);
        Assert.Contains(list!, t => t.TenantId == tenantId && t.Role == "Member");

        using var scope = factory.Services.CreateScope();
        var invitations = scope.ServiceProvider.GetRequiredService<ITenantInvitationStore>();
        var rows = await invitations.ListByTenantAsync(tenantId);
        Assert.Contains(rows, i => i.Status == TenantInvitationStatus.Claimed
                                   && i.ClaimedBySubject == "invitee-sub-1");
    }

    [Fact]
    public async Task JitClaim_CaseInsensitiveEmail_Matches()
    {
        await using var factory = new OidcAuthWebApplicationFactory();
        var owner = factory.CreateAuthenticatedClient("owner-case", Issuer);
        var tenantId = await CreateTenantAsync(owner, "Case Tenant");

        (await owner.PostAsJsonAsync(
            "/v1/invitations",
            new InvitationsController.CreateInvitationsRequest("cased@example.com", [tenantId])))
            .EnsureSuccessStatusCode();

        var invitee = factory.CreateAuthenticatedClient("case-sub", Issuer, email: "CASED@EXAMPLE.COM");
        var list = await invitee.GetFromJsonAsync<List<TenantsController.TenantListItemResponse>>("/v1/tenants");
        Assert.Contains(list!, t => t.TenantId == tenantId);
    }

    [Fact]
    public async Task JitClaim_MultiTenant_ClaimsAllPending()
    {
        await using var factory = new OidcAuthWebApplicationFactory();
        var owner = factory.CreateAuthenticatedClient("owner-multi-claim", Issuer);
        var t1 = await CreateTenantAsync(owner, "T1");
        var t2 = await CreateTenantAsync(owner, "T2");

        (await owner.PostAsJsonAsync(
            "/v1/invitations",
            new InvitationsController.CreateInvitationsRequest("multi-claim@example.com", [t1, t2])))
            .EnsureSuccessStatusCode();

        var invitee = factory.CreateAuthenticatedClient("multi-sub", Issuer, email: "multi-claim@example.com");
        var list = await invitee.GetFromJsonAsync<List<TenantsController.TenantListItemResponse>>("/v1/tenants");
        Assert.Equal(2, list!.Count);
        Assert.Contains(list, t => t.TenantId == t1 && t.Role == "Member");
        Assert.Contains(list, t => t.TenantId == t2 && t.Role == "Member");
    }

    [Fact]
    public async Task JitClaim_MissingEmailClaim_SkipsSilently()
    {
        await using var factory = new OidcAuthWebApplicationFactory();
        var owner = factory.CreateAuthenticatedClient("owner-noemail", Issuer);
        var tenantId = await CreateTenantAsync(owner, "NoEmail");

        (await owner.PostAsJsonAsync(
            "/v1/invitations",
            new InvitationsController.CreateInvitationsRequest("has@example.com", [tenantId])))
            .EnsureSuccessStatusCode();

        var inviteeNoEmail = factory.CreateAuthenticatedClient("no-email-sub", Issuer);
        var list = await inviteeNoEmail.GetFromJsonAsync<List<TenantsController.TenantListItemResponse>>("/v1/tenants");
        Assert.NotNull(list);
        Assert.Empty(list!);

        using var scope = factory.Services.CreateScope();
        var invitations = scope.ServiceProvider.GetRequiredService<ITenantInvitationStore>();
        var rows = await invitations.ListByTenantAsync(tenantId);
        Assert.All(rows, i => Assert.Equal(TenantInvitationStatus.Pending, i.Status));
    }

    [Fact]
    public async Task JitClaim_Revoked_NotReclaimable()
    {
        await using var factory = new OidcAuthWebApplicationFactory();
        var owner = factory.CreateAuthenticatedClient("owner-revoke", Issuer);
        var tenantId = await CreateTenantAsync(owner, "Revoke");

        var created = await owner.PostAsJsonAsync(
            "/v1/invitations",
            new InvitationsController.CreateInvitationsRequest("revoked@example.com", [tenantId]));
        created.EnsureSuccessStatusCode();
        var body = await created.Content.ReadFromJsonAsync<InvitationsController.CreateInvitationsResponse>(JsonOptions);
        var invitationId = body!.Invitations[0].Id;

        var revoke = await owner.PostAsync($"/v1/invitations/{invitationId}/revoke", null);
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);

        var invitee = factory.CreateAuthenticatedClient("revoked-sub", Issuer, email: "revoked@example.com");
        var list = await invitee.GetFromJsonAsync<List<TenantsController.TenantListItemResponse>>("/v1/tenants");
        Assert.DoesNotContain(list!, t => t.TenantId == tenantId);
    }

    [Fact]
    public async Task JitClaim_AlreadyClaimed_NotReclaimableByAnotherSubject()
    {
        await using var factory = new OidcAuthWebApplicationFactory();
        var owner = factory.CreateAuthenticatedClient("owner-claimed", Issuer);
        var tenantId = await CreateTenantAsync(owner, "ClaimedOnce");

        (await owner.PostAsJsonAsync(
            "/v1/invitations",
            new InvitationsController.CreateInvitationsRequest("once@example.com", [tenantId])))
            .EnsureSuccessStatusCode();

        var first = factory.CreateAuthenticatedClient("first-claimer", Issuer, email: "once@example.com");
        (await first.GetAsync("/v1/tenants")).EnsureSuccessStatusCode();

        var second = factory.CreateAuthenticatedClient("second-claimer", Issuer, email: "once@example.com");
        var list = await second.GetFromJsonAsync<List<TenantsController.TenantListItemResponse>>("/v1/tenants");
        Assert.DoesNotContain(list!, t => t.TenantId == tenantId);

        using var scope = factory.Services.CreateScope();
        var memberships = scope.ServiceProvider.GetRequiredService<ITenantMembershipStore>();
        Assert.NotNull(await memberships.GetAsync(tenantId, Issuer, "first-claimer"));
        Assert.Null(await memberships.GetAsync(tenantId, Issuer, "second-claimer"));
    }

    [Fact]
    public async Task JitClaim_ConcurrentClaims_ProduceSingleMembership()
    {
        await using var factory = new OidcAuthWebApplicationFactory();
        var owner = factory.CreateAuthenticatedClient("owner-concurrent", Issuer);
        var tenantId = await CreateTenantAsync(owner, "Concurrent");

        (await owner.PostAsJsonAsync(
            "/v1/invitations",
            new InvitationsController.CreateInvitationsRequest("concurrent@example.com", [tenantId])))
            .EnsureSuccessStatusCode();

        var clients = Enumerable.Range(0, 8)
            .Select(_ => factory.CreateAuthenticatedClient("same-concurrent-sub", Issuer, email: "concurrent@example.com"))
            .ToList();

        var tasks = clients.Select(c => c.GetAsync("/v1/tenants")).ToArray();
        var responses = await Task.WhenAll(tasks);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        using var scope = factory.Services.CreateScope();
        var memberships = scope.ServiceProvider.GetRequiredService<ITenantMembershipStore>();
        var forTenant = await memberships.ListByTenantAsync(tenantId);
        Assert.Single(forTenant, m => m.IdentitySubject == "same-concurrent-sub");
    }

    [Fact]
    public async Task JitClaim_AlreadyMember_Idempotent()
    {
        await using var factory = new OidcAuthWebApplicationFactory();
        var owner = factory.CreateAuthenticatedClient("owner-already", Issuer);
        var tenantId = await CreateTenantAsync(owner, "Already");

        using (var scope = factory.Services.CreateScope())
        {
            var memberships = scope.ServiceProvider.GetRequiredService<ITenantMembershipStore>();
            await memberships.AddAsync(new TenantMembership
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                IdentityIssuer = Issuer,
                IdentitySubject = "already-member",
                Role = TenantMembershipRole.Member,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        (await owner.PostAsJsonAsync(
            "/v1/invitations",
            new InvitationsController.CreateInvitationsRequest("already@example.com", [tenantId])))
            .EnsureSuccessStatusCode();

        var member = factory.CreateAuthenticatedClient("already-member", Issuer, email: "already@example.com");
        var list1 = await member.GetFromJsonAsync<List<TenantsController.TenantListItemResponse>>("/v1/tenants");
        var list2 = await member.GetFromJsonAsync<List<TenantsController.TenantListItemResponse>>("/v1/tenants");
        Assert.Single(list1!, t => t.TenantId == tenantId);
        Assert.Single(list2!, t => t.TenantId == tenantId);
    }

    [Fact]
    public async Task ListInvitations_OwnerSees_MemberForbidden()
    {
        await using var factory = new OidcAuthWebApplicationFactory();
        var owner = factory.CreateAuthenticatedClient("owner-list-inv", Issuer);
        var tenantId = await CreateTenantAsync(owner, "ListInv");

        (await owner.PostAsJsonAsync(
            "/v1/invitations",
            new InvitationsController.CreateInvitationsRequest("list@example.com", [tenantId])))
            .EnsureSuccessStatusCode();

        var ownerList = await owner.GetAsync($"/v1/invitations?tenantId={tenantId}");
        Assert.Equal(HttpStatusCode.OK, ownerList.StatusCode);
        var items = await ownerList.Content.ReadFromJsonAsync<List<InvitationsController.InvitationResponse>>(JsonOptions);
        Assert.Single(items!);

        using (var scope = factory.Services.CreateScope())
        {
            var memberships = scope.ServiceProvider.GetRequiredService<ITenantMembershipStore>();
            await memberships.AddAsync(new TenantMembership
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                IdentityIssuer = Issuer,
                IdentitySubject = "member-list",
                Role = TenantMembershipRole.Member,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        var member = factory.CreateAuthenticatedClient("member-list", Issuer);
        var memberList = await member.GetAsync($"/v1/invitations?tenantId={tenantId}");
        Assert.Equal(HttpStatusCode.Forbidden, memberList.StatusCode);

        var stranger = factory.CreateAuthenticatedClient("stranger-list", Issuer);
        var strangerList = await stranger.GetAsync($"/v1/invitations?tenantId={tenantId}");
        Assert.Equal(HttpStatusCode.Forbidden, strangerList.StatusCode);
    }

    [Fact]
    public async Task Revoke_MemberForbidden_OwnerSucceeds_DoesNotTouchMembership()
    {
        await using var factory = new OidcAuthWebApplicationFactory();
        var owner = factory.CreateAuthenticatedClient("owner-rev2", Issuer);
        var tenantId = await CreateTenantAsync(owner, "Rev2");

        var created = await owner.PostAsJsonAsync(
            "/v1/invitations",
            new InvitationsController.CreateInvitationsRequest("keep-member@example.com", [tenantId]));
        var body = await created.Content.ReadFromJsonAsync<InvitationsController.CreateInvitationsResponse>(JsonOptions);
        var invitationId = body!.Invitations[0].Id;

        var invitee = factory.CreateAuthenticatedClient("keep-member-sub", Issuer, email: "keep-member@example.com");
        (await invitee.GetAsync("/v1/tenants")).EnsureSuccessStatusCode();

        // After claim, revoke of Pending fails (already Claimed).
        var revokeClaimed = await owner.PostAsync($"/v1/invitations/{invitationId}/revoke", null);
        Assert.Equal(HttpStatusCode.Conflict, revokeClaimed.StatusCode);

        using var scope = factory.Services.CreateScope();
        var memberships = scope.ServiceProvider.GetRequiredService<ITenantMembershipStore>();
        Assert.NotNull(await memberships.GetAsync(tenantId, Issuer, "keep-member-sub"));
    }

    [Fact]
    public async Task EmailChangeAfterClaim_KeepsAccessViaIssSub()
    {
        await using var factory = new OidcAuthWebApplicationFactory();
        var owner = factory.CreateAuthenticatedClient("owner-email-change", Issuer);
        var tenantId = await CreateTenantAsync(owner, "EmailChange");

        (await owner.PostAsJsonAsync(
            "/v1/invitations",
            new InvitationsController.CreateInvitationsRequest("old@example.com", [tenantId])))
            .EnsureSuccessStatusCode();

        var user = factory.CreateAuthenticatedClient("stable-sub", Issuer, email: "old@example.com");
        (await user.GetAsync("/v1/tenants")).EnsureSuccessStatusCode();

        // Same iss/sub, different email claim — membership is identity-stable.
        var userNewEmail = factory.CreateAuthenticatedClient("stable-sub", Issuer, email: "new@example.com");
        var list = await userNewEmail.GetFromJsonAsync<List<TenantsController.TenantListItemResponse>>("/v1/tenants");
        Assert.Contains(list!, t => t.TenantId == tenantId && t.Role == "Member");
    }

    [Fact]
    public async Task IdPRole_DoesNotGrantTenantAccess_WithoutMembership()
    {
        await using var factory = new OidcAuthWebApplicationFactory(builder =>
        {
            builder.UseSetting("Authorization:TenantCreation:Mode", "Role");
            builder.UseSetting("Authorization:TenantCreation:RequiredRole", "platform-tenant-admin");
        });

        var owner = factory.CreateAuthenticatedClient(
            "idp-owner",
            Issuer,
            role: "platform-tenant-admin");
        var tenantId = await CreateTenantAsync(owner, "IdP Sep");

        // User with TenantCreation IdP role but no membership.
        var idpAdmin = factory.CreateAuthenticatedClient(
            "idp-only",
            Issuer,
            role: "platform-tenant-admin");
        var services = await idpAdmin.GetAsync($"/v1/tenants/{tenantId}/services");
        Assert.Equal(HttpStatusCode.NotFound, services.StatusCode);

        var list = await idpAdmin.GetFromJsonAsync<List<TenantsController.TenantListItemResponse>>("/v1/tenants");
        Assert.Empty(list!);
    }

    [Fact]
    public async Task LosingIdPRole_DoesNotRemoveOwnerMembership()
    {
        await using var factory = new OidcAuthWebApplicationFactory(builder =>
        {
            builder.UseSetting("Authorization:TenantCreation:Mode", "Role");
            builder.UseSetting("Authorization:TenantCreation:RequiredRole", "platform-tenant-admin");
        });

        var withRole = factory.CreateAuthenticatedClient(
            "role-then-lost",
            Issuer,
            role: "platform-tenant-admin");
        var tenantId = await CreateTenantAsync(withRole, "Keep Owner");

        // Same subject without IdP role still has Owner membership.
        var withoutRole = factory.CreateAuthenticatedClient("role-then-lost", Issuer);
        var list = await withoutRole.GetFromJsonAsync<List<TenantsController.TenantListItemResponse>>("/v1/tenants");
        Assert.Contains(list!, t => t.TenantId == tenantId && t.Role == "Owner");

        var services = await withoutRole.GetAsync($"/v1/tenants/{tenantId}/services");
        Assert.Equal(HttpStatusCode.OK, services.StatusCode);

        // Cannot create new tenants without role, but existing ownership remains.
        var createDenied = await withoutRole.PostAsJsonAsync(
            "/v1/tenants",
            new TenantsController.CreateTenantRequest("Should Fail"));
        Assert.Equal(HttpStatusCode.Forbidden, createDenied.StatusCode);
    }

    [Fact]
    public async Task Member_CannotManageApiKeys_ViaJwt_OwnerCan()
    {
        await using var factory = new OidcAuthWebApplicationFactory();
        var owner = factory.CreateAuthenticatedClient("owner-keys", Issuer);
        var tenantId = await CreateTenantAsync(owner, "Keys");

        using (var scope = factory.Services.CreateScope())
        {
            var memberships = scope.ServiceProvider.GetRequiredService<ITenantMembershipStore>();
            await memberships.AddAsync(new TenantMembership
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                IdentityIssuer = Issuer,
                IdentitySubject = "member-keys",
                Role = TenantMembershipRole.Member,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        var member = factory.CreateAuthenticatedClient("member-keys", Issuer);
        var memberCreate = await member.PostAsJsonAsync(
            $"/v1/api-keys?tenantId={tenantId}",
            new ApiKeysController.CreateApiKeyRequest("nope"));
        Assert.Equal(HttpStatusCode.Forbidden, memberCreate.StatusCode);

        var ownerCreate = await owner.PostAsJsonAsync(
            $"/v1/api-keys?tenantId={tenantId}",
            new ApiKeysController.CreateApiKeyRequest("owner-key"));
        Assert.Equal(HttpStatusCode.Created, ownerCreate.StatusCode);
    }

    [Fact]
    public async Task UnrelatedUser_SeesNoTenants()
    {
        await using var factory = new OidcAuthWebApplicationFactory();
        var owner = factory.CreateAuthenticatedClient("owner-vis", Issuer);
        await CreateTenantAsync(owner, "Visible only to owner");

        var stranger = factory.CreateAuthenticatedClient("stranger-vis", Issuer);
        var list = await stranger.GetFromJsonAsync<List<TenantsController.TenantListItemResponse>>("/v1/tenants");
        Assert.Empty(list!);
    }

    [Fact]
    public async Task CreateInvitation_MultiTenant_SuccessForAllOwned()
    {
        await using var factory = new OidcAuthWebApplicationFactory();
        var owner = factory.CreateAuthenticatedClient("owner-both", Issuer);
        var t1 = await CreateTenantAsync(owner, "Both1");
        var t2 = await CreateTenantAsync(owner, "Both2");

        var response = await owner.PostAsJsonAsync(
            "/v1/invitations",
            new InvitationsController.CreateInvitationsRequest("both@example.com", [t1, t2]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<InvitationsController.CreateInvitationsResponse>(JsonOptions);
        Assert.Equal(2, body!.Invitations.Count);
    }

    private static async Task<Guid> CreateTenantAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync(
            "/v1/tenants",
            new TenantsController.CreateTenantRequest(name));
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<TenantsController.CreateTenantResponse>();
        Assert.NotNull(created);
        return created!.TenantId;
    }
}
