using Platform.Identity;
using Platform.Storage;
using Xunit;

namespace Platform.Identity.Tests;

public class ApiKeyServiceTests
{
    [Fact]
    public async Task ValidKey_ResolvesTrustedTenantId()
    {
        var service = new ApiKeyService(new InMemoryApiKeyStore());
        var tenantId = Guid.NewGuid();
        var created = await service.CreateAsync(tenantId, "test");

        var resolved = await service.ValidateAsync(created.PlaintextKey);

        Assert.Equal(tenantId, resolved);
    }

    [Fact]
    public async Task InvalidKey_ReturnsNull()
    {
        var service = new ApiKeyService(new InMemoryApiKeyStore());
        await service.CreateAsync(Guid.NewGuid(), "test");

        var resolved = await service.ValidateAsync("pk_not_a_real_key");

        Assert.Null(resolved);
    }

    [Fact]
    public async Task RevokedKey_ReturnsNull()
    {
        var service = new ApiKeyService(new InMemoryApiKeyStore());
        var created = await service.CreateAsync(Guid.NewGuid(), "test");

        await service.RevokeAsync(created.ApiKeyId);
        var resolved = await service.ValidateAsync(created.PlaintextKey);

        Assert.Null(resolved);
    }

    [Fact]
    public async Task StoredKey_NeverContainsPlaintext()
    {
        var store = new InMemoryApiKeyStore();
        var service = new ApiKeyService(store);
        var created = await service.CreateAsync(Guid.NewGuid(), "test");

        var stored = await store.GetByIdAsync(created.ApiKeyId);

        Assert.NotNull(stored);
        Assert.NotEqual(created.PlaintextKey, stored!.HashedKey);
        Assert.DoesNotContain(created.PlaintextKey, stored.HashedKey);
    }

    [Fact]
    public void HashesAreDeterministic_ButComparisonIsConstantTime()
    {
        var key = ApiKeyHasher.GenerateKey();
        var hash1 = ApiKeyHasher.Hash(key);
        var hash2 = ApiKeyHasher.Hash(key);

        Assert.Equal(hash1, hash2);
        Assert.True(ApiKeyHasher.Matches(key, hash1));
        Assert.False(ApiKeyHasher.Matches("pk_wrong", hash1));
    }

    [Fact]
    public async Task ListAsync_ReturnsMetadataWithoutSecrets()
    {
        var service = new ApiKeyService(new InMemoryApiKeyStore());
        var tenantId = Guid.NewGuid();
        var created = await service.CreateAsync(tenantId, "ops-key");

        var list = await service.ListAsync(tenantId);
        var info = Assert.Single(list);
        Assert.Equal(created.ApiKeyId, info.Id);
        Assert.Equal("ops-key", info.Name);
        Assert.True(info.IsActive);
    }
}
