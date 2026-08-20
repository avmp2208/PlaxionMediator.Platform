using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Platform.Api.Controllers;
using Platform.Identity;
using Platform.Storage;
using Xunit;

namespace Platform.Api.Tests;

/// <summary>
/// Phase 3: legacy-tenant upgrade / platform-administrator reconciliation.
/// </summary>
public sealed class LegacyTenantBootstrapTests
{
    private const string Issuer = "https://test-issuer/";
    private const string PlatformAdminRole = "platform-admin";

    [Fact]
    public async Task LegacyTenant_WithoutMembership_IsInaccessibleToRandomJwtUser()
    {
        await using var factory = CreateFactoryWithPlatformAdminRole();
        var legacyTenantId = await SeedLegacyTenantAsync(factory);

        var stranger = factory.CreateAuthenticatedClient(subject: "random-user", issuer: Issuer);
        var response = await stranger.GetAsync($"/v1/tenants/{legacyTenantId}/services");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PlatformAdministrator_CanAccessLegacyTenant_WithoutMembership()
    {
        await using var factory = CreateFactoryWithPlatformAdminRole();
        var legacyTenantId = await SeedLegacyTenantAsync(factory, serviceName: "legacy-svc");

        var admin = factory.CreateAuthenticatedClient(
            subject: "ops-admin",
            issuer: Issuer,
            role: PlatformAdminRole);

        var response = await admin.GetAsync($"/v1/tenants/{legacyTenantId}/services");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var services = await response.Content.ReadFromJsonAsync<List<ServiceResponse>>();
        Assert.Contains(services!, s => s.Name == "legacy-svc");
    }

    [Fact]
    public async Task NonAdministrator_CannotCallMembershipReconciliation_Returns403()
    {
        await using var factory = CreateFactoryWithPlatformAdminRole();
        var legacyTenantId = await SeedLegacyTenantAsync(factory);

        var nonAdmin = factory.CreateAuthenticatedClient(subject: "not-admin", issuer: Issuer);
        var response = await nonAdmin.PostAsJsonAsync(
            $"/v1/tenants/{legacyTenantId}/memberships",
            new TenantMembershipsController.CreateMembershipRequest(null, null, TenantMembershipRole.Owner));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_CannotCallMembershipReconciliation_Returns401()
    {
        await using var factory = CreateFactoryWithPlatformAdminRole();
        var legacyTenantId = await SeedLegacyTenantAsync(factory);

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/v1/tenants/{legacyTenantId}/memberships",
            new TenantMembershipsController.CreateMembershipRequest(null, null, null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PlatformAdministrator_CanSelfGrantOwnerMembership_OnLegacyTenant()
    {
        await using var factory = CreateFactoryWithPlatformAdminRole();
        var legacyTenantId = await SeedLegacyTenantAsync(factory);

        var admin = factory.CreateAuthenticatedClient(
            subject: "ops-self-grant",
            issuer: Issuer,
            role: PlatformAdminRole);

        // Empty body: defaults to caller's iss/sub and Owner role.
        var response = await admin.PostAsJsonAsync(
            $"/v1/tenants/{legacyTenantId}/memberships",
            new TenantMembershipsController.CreateMembershipRequest(null, null, null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var membership = await response.Content
            .ReadFromJsonAsync<TenantMembershipsController.MembershipResponse>();
        Assert.NotNull(membership);
        Assert.Equal(legacyTenantId, membership!.TenantId);
        Assert.Equal(Issuer, membership.IdentityIssuer);
        Assert.Equal("ops-self-grant", membership.IdentitySubject);
        Assert.Equal(TenantMembershipRole.Owner, membership.Role);

        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ITenantMembershipStore>();
        var stored = await store.GetAsync(legacyTenantId, Issuer, "ops-self-grant");
        Assert.NotNull(stored);
        Assert.Equal(TenantMembershipRole.Owner, stored!.Role);
    }

    [Fact]
    public async Task PlatformAdministrator_CanGrantMembership_ToSpecifiedIdentity()
    {
        await using var factory = CreateFactoryWithPlatformAdminRole();
        var legacyTenantId = await SeedLegacyTenantAsync(factory);

        var admin = factory.CreateAuthenticatedClient(
            subject: "ops-grantor",
            issuer: Issuer,
            role: PlatformAdminRole);

        var response = await admin.PostAsJsonAsync(
            $"/v1/tenants/{legacyTenantId}/memberships",
            new TenantMembershipsController.CreateMembershipRequest(
                Issuer,
                "target-owner",
                TenantMembershipRole.Owner));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ITenantMembershipStore>();
        var stored = await store.GetAsync(legacyTenantId, Issuer, "target-owner");
        Assert.NotNull(stored);
        Assert.Equal(TenantMembershipRole.Owner, stored!.Role);
    }

    [Fact]
    public async Task AfterReconciliation_PreviouslyBlockedUser_CanAccessViaTenantMember()
    {
        await using var factory = CreateFactoryWithPlatformAdminRole();
        var legacyTenantId = await SeedLegacyTenantAsync(factory, serviceName: "post-reconcile-svc");

        const string ownerSubject = "legacy-owner";
        var ownerClient = factory.CreateAuthenticatedClient(subject: ownerSubject, issuer: Issuer);

        // Before reconciliation: blocked.
        var before = await ownerClient.GetAsync($"/v1/tenants/{legacyTenantId}/services");
        Assert.Equal(HttpStatusCode.NotFound, before.StatusCode);

        var admin = factory.CreateAuthenticatedClient(
            subject: "ops-reconcile",
            issuer: Issuer,
            role: PlatformAdminRole);

        var grant = await admin.PostAsJsonAsync(
            $"/v1/tenants/{legacyTenantId}/memberships",
            new TenantMembershipsController.CreateMembershipRequest(
                Issuer,
                ownerSubject,
                TenantMembershipRole.Owner));
        Assert.Equal(HttpStatusCode.Created, grant.StatusCode);

        // After reconciliation: standard TenantMember path succeeds.
        var after = await ownerClient.GetAsync($"/v1/tenants/{legacyTenantId}/services");
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        var services = await after.Content.ReadFromJsonAsync<List<ServiceResponse>>();
        Assert.Contains(services!, s => s.Name == "post-reconcile-svc");
    }

    [Fact]
    public async Task DuplicateMembershipGrant_Returns409()
    {
        await using var factory = CreateFactoryWithPlatformAdminRole();
        var legacyTenantId = await SeedLegacyTenantAsync(factory);

        var admin = factory.CreateAuthenticatedClient(
            subject: "ops-dup",
            issuer: Issuer,
            role: PlatformAdminRole);

        var first = await admin.PostAsJsonAsync(
            $"/v1/tenants/{legacyTenantId}/memberships",
            new TenantMembershipsController.CreateMembershipRequest(null, null, TenantMembershipRole.Owner));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await admin.PostAsJsonAsync(
            $"/v1/tenants/{legacyTenantId}/memberships",
            new TenantMembershipsController.CreateMembershipRequest(null, null, TenantMembershipRole.Owner));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task PlatformAdministrator_ClaimMode_CanReconcileLegacyTenant()
    {
        await using var factory = new OidcAuthWebApplicationFactory(builder =>
        {
            builder.UseSetting("Authorization:PlatformAdministrator:Mode", "Claim");
            builder.UseSetting("Authorization:PlatformAdministrator:RequiredClaimType", "groups");
            builder.UseSetting("Authorization:PlatformAdministrator:RequiredClaimValues:0", "platform-ops");
        });

        var legacyTenantId = await SeedLegacyTenantAsync(factory, serviceName: "claim-admin-svc");

        var admin = factory.CreateAuthenticatedClient(
            subject: "claim-admin",
            issuer: Issuer,
            claimType: "groups",
            claimValue: "platform-ops");

        var access = await admin.GetAsync($"/v1/tenants/{legacyTenantId}/services");
        Assert.Equal(HttpStatusCode.OK, access.StatusCode);

        var grant = await admin.PostAsJsonAsync(
            $"/v1/tenants/{legacyTenantId}/memberships",
            new TenantMembershipsController.CreateMembershipRequest(
                Issuer,
                "claim-granted-user",
                TenantMembershipRole.Member));
        Assert.Equal(HttpStatusCode.Created, grant.StatusCode);
    }

    private static OidcAuthWebApplicationFactory CreateFactoryWithPlatformAdminRole() =>
        new(builder =>
        {
            builder.UseSetting("Authorization:PlatformAdministrator:Mode", "Role");
            builder.UseSetting("Authorization:PlatformAdministrator:RequiredRole", PlatformAdminRole);
        });

    /// <summary>
    /// Simulates a pre-OIDC tenant: API key (+ optional service) exist, but
    /// zero TenantMembership rows.
    /// </summary>
    private static async Task<Guid> SeedLegacyTenantAsync(
        OidcAuthWebApplicationFactory factory,
        string? serviceName = null)
    {
        var tenantId = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        var apiKeys = scope.ServiceProvider.GetRequiredService<ApiKeyService>();
        await apiKeys.CreateAsync(tenantId, "Legacy Default");

        if (!string.IsNullOrWhiteSpace(serviceName))
        {
            var catalog = scope.ServiceProvider.GetRequiredService<IServiceCatalog>();
            await catalog.GetOrCreateAsync(tenantId, serviceName, "prod");
        }

        var memberships = scope.ServiceProvider.GetRequiredService<ITenantMembershipStore>();
        var existing = await memberships.ListByTenantAsync(tenantId);
        Assert.Empty(existing);

        return tenantId;
    }
}
