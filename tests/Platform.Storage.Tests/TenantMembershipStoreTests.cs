using Platform.Storage;
using Xunit;

namespace Platform.Storage.Tests;

public class TenantMembershipStoreTests
{
    [Fact]
    public async Task Add_And_GetByIdentity_ReturnsMembership()
    {
        var store = new InMemoryTenantMembershipStore();
        var tenantId = Guid.NewGuid();
        var membership = new TenantMembership
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            IdentityIssuer = "https://issuer.example.com/",
            IdentitySubject = "user-123",
            Role = TenantMembershipRole.Owner,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await store.AddAsync(membership);

        var loaded = await store.GetAsync(tenantId, membership.IdentityIssuer, membership.IdentitySubject);
        Assert.NotNull(loaded);
        Assert.Equal(membership.Id, loaded!.Id);
        Assert.Equal(TenantMembershipRole.Owner, loaded.Role);
    }

    [Fact]
    public async Task ListByTenant_OnlyReturnsOwnMemberships()
    {
        var store = new InMemoryTenantMembershipStore();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await store.AddAsync(new TenantMembership
        {
            Id = Guid.NewGuid(),
            TenantId = tenantA,
            IdentityIssuer = "https://issuer.example.com/",
            IdentitySubject = "user-a",
            Role = TenantMembershipRole.Owner,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await store.AddAsync(new TenantMembership
        {
            Id = Guid.NewGuid(),
            TenantId = tenantB,
            IdentityIssuer = "https://issuer.example.com/",
            IdentitySubject = "user-b",
            Role = TenantMembershipRole.Member,
            CreatedAt = DateTimeOffset.UtcNow
        });

        var forA = await store.ListByTenantAsync(tenantA);
        var single = Assert.Single(forA);
        Assert.Equal("user-a", single.IdentitySubject);
    }

    [Fact]
    public async Task ListByIdentity_ReturnsAllTenantsForSubject()
    {
        var store = new InMemoryTenantMembershipStore();
        const string issuer = "https://issuer.example.com/";
        const string subject = "multi-tenant-user";
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await store.AddAsync(new TenantMembership
        {
            Id = Guid.NewGuid(),
            TenantId = tenantA,
            IdentityIssuer = issuer,
            IdentitySubject = subject,
            Role = TenantMembershipRole.Owner,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await store.AddAsync(new TenantMembership
        {
            Id = Guid.NewGuid(),
            TenantId = tenantB,
            IdentityIssuer = issuer,
            IdentitySubject = subject,
            Role = TenantMembershipRole.Member,
            CreatedAt = DateTimeOffset.UtcNow.AddSeconds(1)
        });
        await store.AddAsync(new TenantMembership
        {
            Id = Guid.NewGuid(),
            TenantId = tenantA,
            IdentityIssuer = issuer,
            IdentitySubject = "other-user",
            Role = TenantMembershipRole.Member,
            CreatedAt = DateTimeOffset.UtcNow
        });

        var memberships = await store.ListByIdentityAsync(issuer, subject);
        Assert.Equal(2, memberships.Count);
        Assert.Contains(memberships, m => m.TenantId == tenantA && m.Role == TenantMembershipRole.Owner);
        Assert.Contains(memberships, m => m.TenantId == tenantB && m.Role == TenantMembershipRole.Member);
    }

    [Fact]
    public async Task GetAsync_UnknownIdentity_ReturnsNull()
    {
        var store = new InMemoryTenantMembershipStore();
        var result = await store.GetAsync(Guid.NewGuid(), "https://issuer.example.com/", "missing");
        Assert.Null(result);
    }
}
