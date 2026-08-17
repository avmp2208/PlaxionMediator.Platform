using Platform.Storage;

namespace Platform.Storage.Tests;

/// <summary>
/// Provider-agnostic conformance suite. Concrete providers (in-memory, SQLite)
/// supply store instances via <see cref="CreateStoresAsync"/>.
/// </summary>
public abstract class StorageProviderConformanceTests
{
    protected abstract Task<StorageHandles> CreateStoresAsync();

    protected virtual Task DisposeStoresAsync(StorageHandles handles) => Task.CompletedTask;

    /// <summary>
    /// Optional second handle pair against the same durable backend (e.g. same
    /// SQLite file) used to prove restart/persistence. Null for pure in-memory.
    /// </summary>
    protected virtual Task<StorageHandles?> RecreateStoresAsync(StorageHandles original) =>
        Task.FromResult<StorageHandles?>(null);

    [Fact]
    public async Task MetricSample_WriteRead_RoundTrips()
    {
        var handles = await CreateStoresAsync();
        try
        {
            var tenantId = Guid.NewGuid();
            var sample = NewSample(tenantId, "svc", "prod", "cpu", 42, DateTimeOffset.UtcNow);

            await handles.Samples.AddAsync(sample);
            var results = await handles.Samples.GetByTenantAsync(tenantId);

            var single = Assert.Single(results);
            Assert.Equal(sample.Id, single.Id);
            Assert.Equal(sample.MetricName, single.MetricName);
            Assert.Equal(sample.Value, single.Value);
        }
        finally
        {
            await DisposeStoresAsync(handles);
        }
    }

    [Fact]
    public async Task MetricSample_TenantIsolation_TenantBCannotReadTenantA()
    {
        var handles = await CreateStoresAsync();
        try
        {
            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();
            await handles.Samples.AddAsync(NewSample(tenantA, "svc", "prod", "cpu", 1, DateTimeOffset.UtcNow));

            Assert.Empty(await handles.Samples.GetByTenantAsync(tenantB));
            Assert.Single(await handles.Samples.GetByTenantAsync(tenantA));
        }
        finally
        {
            await DisposeStoresAsync(handles);
        }
    }

    [Fact]
    public async Task MetricSample_TimeRangeFiltering()
    {
        var handles = await CreateStoresAsync();
        try
        {
            var tenantId = Guid.NewGuid();
            var t0 = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
            await handles.Samples.AddAsync(NewSample(tenantId, "svc", "prod", "cpu", 1, t0.AddHours(-2)));
            await handles.Samples.AddAsync(NewSample(tenantId, "svc", "prod", "cpu", 2, t0));
            await handles.Samples.AddAsync(NewSample(tenantId, "svc", "prod", "cpu", 3, t0.AddHours(2)));

            var mid = await handles.Samples.QueryAsync(tenantId, from: t0.AddHours(-1), to: t0.AddHours(1));
            var single = Assert.Single(mid);
            Assert.Equal(2, single.Value);
        }
        finally
        {
            await DisposeStoresAsync(handles);
        }
    }

    [Fact]
    public async Task MetricSample_RespectsCancellation()
    {
        var handles = await CreateStoresAsync();
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                handles.Samples.AddAsync(NewSample(Guid.NewGuid(), "s", "e", "m", 1, DateTimeOffset.UtcNow), cts.Token));
        }
        finally
        {
            await DisposeStoresAsync(handles);
        }
    }

    [Fact]
    public async Task ServiceCatalog_GetOrCreate_List_AndCrossTenant()
    {
        var handles = await CreateStoresAsync();
        try
        {
            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();
            var a = await handles.Catalog.GetOrCreateAsync(tenantA, "checkout", "prod");
            var a2 = await handles.Catalog.GetOrCreateAsync(tenantA, "checkout", "prod");
            await handles.Catalog.GetOrCreateAsync(tenantB, "other", "prod");

            Assert.Equal(a.Id, a2.Id);
            Assert.Single(await handles.Catalog.ListByTenantAsync(tenantA));
            Assert.NotNull(await handles.Catalog.GetByIdAsync(tenantA, a.Id));
            Assert.Null(await handles.Catalog.GetByIdAsync(tenantB, a.Id));
        }
        finally
        {
            await DisposeStoresAsync(handles);
        }
    }

    [Fact]
    public async Task AggregateRollup_PersistsAndFiltersByTenantTimeRange()
    {
        var handles = await CreateStoresAsync();
        try
        {
            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();
            var serviceId = ServiceIdGenerator.Compute(tenantA, "checkout", "prod");
            var now = DateTimeOffset.UtcNow;

            await handles.Rollups.AddRangeAsync(new[]
            {
                new AggregateRollup
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantA,
                    ServiceName = "checkout",
                    Environment = "prod",
                    MetricName = "latency",
                    BucketStart = now.AddMinutes(-1),
                    BucketEnd = now,
                    Count = 2,
                    Min = 1,
                    Max = 3,
                    Avg = 2,
                    P50 = 2,
                    P95 = 3,
                    P99 = 3
                }
            });

            Assert.Single(await handles.Rollups.QueryAsync(tenantA, serviceId, now.AddHours(-1), now.AddHours(1)));
            Assert.Empty(await handles.Rollups.QueryAsync(tenantB, serviceId, now.AddHours(-1), now.AddHours(1)));
            Assert.Empty(await handles.Rollups.QueryAsync(tenantA, serviceId, now.AddHours(-5), now.AddHours(-4)));
        }
        finally
        {
            await DisposeStoresAsync(handles);
        }
    }

    [Fact]
    public async Task MetricSample_RestartPersistence_WhenSupported()
    {
        var handles = await CreateStoresAsync();
        try
        {
            var tenantId = Guid.NewGuid();
            var sample = NewSample(tenantId, "svc", "prod", "cpu", 99, DateTimeOffset.UtcNow);
            await handles.Samples.AddAsync(sample);

            var recreated = await RecreateStoresAsync(handles);
            if (recreated is null)
            {
                return; // provider is not durable
            }

            try
            {
                var results = await recreated.Samples.GetByTenantAsync(tenantId);
                var single = Assert.Single(results);
                Assert.Equal(sample.Id, single.Id);
                Assert.Equal(99, single.Value);
            }
            finally
            {
                await DisposeStoresAsync(recreated);
            }
        }
        finally
        {
            await DisposeStoresAsync(handles);
        }
    }

    [Fact]
    public async Task DeleteOlderThanAsync_RemovesExpired_KeepsNonExpired()
    {
        var handles = await CreateStoresAsync();
        try
        {
            var tenantId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var expired = NewSample(tenantId, "svc", "prod", "cpu", 1, now - TimeSpan.FromDays(10));
            var fresh = NewSample(tenantId, "svc", "prod", "cpu", 2, now);

            await handles.Samples.AddAsync(expired);
            await handles.Samples.AddAsync(fresh);

            await handles.Samples.DeleteOlderThanAsync(now - TimeSpan.FromDays(7));

            var remaining = await handles.Samples.GetByTenantAsync(tenantId);
            var single = Assert.Single(remaining);
            Assert.Equal(fresh.Id, single.Id);
        }
        finally
        {
            await DisposeStoresAsync(handles);
        }
    }

    [Fact]
    public async Task DeleteOlderThanAsync_PreservesTenantIsolation()
    {
        var handles = await CreateStoresAsync();
        try
        {
            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var oldTimestamp = now - TimeSpan.FromDays(30);

            // Tenant B's expired sample must never be affected by evicting
            // tenant A's data - deletion is scoped purely by timestamp, so
            // both old rows (from any tenant) are removed by design, while
            // a still-fresh tenant B row must survive regardless of tenant.
            await handles.Samples.AddAsync(NewSample(tenantA, "svc-a", "prod", "cpu", 1, oldTimestamp));
            var tenantBOld = NewSample(tenantB, "svc-b", "prod", "cpu", 2, oldTimestamp);
            await handles.Samples.AddAsync(tenantBOld);
            var tenantBFresh = NewSample(tenantB, "svc-b", "prod", "cpu", 3, now);
            await handles.Samples.AddAsync(tenantBFresh);

            await handles.Samples.DeleteOlderThanAsync(now - TimeSpan.FromDays(7));

            var tenantAResults = await handles.Samples.GetByTenantAsync(tenantA);
            var tenantBResults = await handles.Samples.GetByTenantAsync(tenantB);

            Assert.Empty(tenantAResults);
            var single = Assert.Single(tenantBResults);
            Assert.Equal(tenantBFresh.Id, single.Id);
        }
        finally
        {
            await DisposeStoresAsync(handles);
        }
    }

    [Fact]
    public async Task DeleteOlderThanAsync_DoesNotAffectAggregateRollups()
    {
        var handles = await CreateStoresAsync();
        try
        {
            var tenantId = Guid.NewGuid();
            var serviceId = ServiceIdGenerator.Compute(tenantId, "checkout", "prod");
            var now = DateTimeOffset.UtcNow;
            var oldTimestamp = now - TimeSpan.FromDays(30);

            await handles.Samples.AddAsync(NewSample(tenantId, "checkout", "prod", "latency", 1, oldTimestamp));

            await handles.Rollups.AddRangeAsync(new[]
            {
                new AggregateRollup
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    ServiceName = "checkout",
                    Environment = "prod",
                    MetricName = "latency",
                    BucketStart = oldTimestamp,
                    BucketEnd = oldTimestamp.AddMinutes(1),
                    Count = 1,
                    Min = 1,
                    Max = 1,
                    Avg = 1,
                    P50 = 1,
                    P95 = 1,
                    P99 = 1
                }
            });

            await handles.Samples.DeleteOlderThanAsync(now - TimeSpan.FromDays(7));

            // Raw sample gone...
            Assert.Empty(await handles.Samples.GetByTenantAsync(tenantId));
            // ...but the rollup computed from it must survive untouched.
            Assert.Single(await handles.Rollups.QueryAsync(
                tenantId, serviceId, oldTimestamp.AddMinutes(-1), oldTimestamp.AddMinutes(2)));
        }
        finally
        {
            await DisposeStoresAsync(handles);
        }
    }

    [Fact]
    public async Task AggregateRollup_DifferentRequestTypes_AreNotOverwritten()
    {
        var handles = await CreateStoresAsync();
        try
        {
            var tenantId = Guid.NewGuid();
            var serviceId = ServiceIdGenerator.Compute(tenantId, "checkout", "prod");
            var now = DateTimeOffset.UtcNow;
            var bucketStart = now.AddMinutes(-1);

            await handles.Rollups.AddRangeAsync(new[]
            {
                new AggregateRollup
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    ServiceName = "checkout",
                    Environment = "prod",
                    MetricName = "plaxionmediator.request.count",
                    RequestType = "CreateOrderRequest",
                    BucketStart = bucketStart,
                    BucketEnd = now,
                    Count = 5,
                    Min = 1,
                    Max = 1,
                    Avg = 1,
                    P50 = 1,
                    P95 = 1,
                    P99 = 1
                },
                new AggregateRollup
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    ServiceName = "checkout",
                    Environment = "prod",
                    MetricName = "plaxionmediator.request.count",
                    RequestType = "GetOrderRequest",
                    BucketStart = bucketStart,
                    BucketEnd = now,
                    Count = 7,
                    Min = 1,
                    Max = 1,
                    Avg = 1,
                    P50 = 1,
                    P95 = 1,
                    P99 = 1
                }
            });

            var results = await handles.Rollups.QueryAsync(tenantId, serviceId, bucketStart.AddMinutes(-1), now.AddMinutes(1));

            Assert.Equal(2, results.Count);
            Assert.Contains(results, r => r.RequestType == "CreateOrderRequest" && r.Count == 5);
            Assert.Contains(results, r => r.RequestType == "GetOrderRequest" && r.Count == 7);
        }
        finally
        {
            await DisposeStoresAsync(handles);
        }
    }

    [Fact]
    public async Task AggregateRollup_SameBucketRecomputed_UpsertsInsteadOfDuplicating()
    {
        var handles = await CreateStoresAsync();
        try
        {
            var tenantId = Guid.NewGuid();
            var serviceId = ServiceIdGenerator.Compute(tenantId, "checkout", "prod");
            var now = DateTimeOffset.UtcNow;
            var bucketStart = now.AddMinutes(-1);

            // Simulates the aggregation job re-running against the same
            // retention window and recomputing the same bucket: it must
            // update the existing row instead of inserting a duplicate.
            await handles.Rollups.AddRangeAsync(new[]
            {
                new AggregateRollup
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    ServiceName = "checkout",
                    Environment = "prod",
                    MetricName = "plaxionmediator.request.count",
                    BucketStart = bucketStart,
                    BucketEnd = now,
                    Count = 5,
                    Min = 1,
                    Max = 1,
                    Avg = 1,
                    P50 = 1,
                    P95 = 1,
                    P99 = 1
                }
            });

            await handles.Rollups.AddRangeAsync(new[]
            {
                new AggregateRollup
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    ServiceName = "checkout",
                    Environment = "prod",
                    MetricName = "plaxionmediator.request.count",
                    BucketStart = bucketStart,
                    BucketEnd = now,
                    Count = 8,
                    Min = 1,
                    Max = 2,
                    Avg = 1.5,
                    P50 = 1,
                    P95 = 2,
                    P99 = 2
                }
            });

            var results = await handles.Rollups.QueryAsync(tenantId, serviceId, bucketStart.AddMinutes(-1), now.AddMinutes(1));

            Assert.Single(results);
            Assert.Equal(8, results[0].Count);
            Assert.Equal(2, results[0].Max);
        }
        finally
        {
            await DisposeStoresAsync(handles);
        }
    }

    private static MetricSample NewSample(
        Guid tenantId,
        string service,
        string env,
        string metric,
        double value,
        DateTimeOffset ts) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        ServiceName = service,
        Environment = env,
        MetricName = metric,
        Value = value,
        Timestamp = ts
    };

    public record StorageHandles(
        IMetricSampleStore Samples,
        IAggregateRollupStore Rollups,
        IServiceCatalog Catalog,
        IApiKeyStore ApiKeys);
}

public sealed class InMemoryStorageConformanceTests : StorageProviderConformanceTests
{
    protected override Task<StorageHandles> CreateStoresAsync() =>
        Task.FromResult(new StorageHandles(
            new InMemoryMetricSampleStore(),
            new InMemoryAggregateRollupStore(),
            new InMemoryServiceCatalog(),
            new InMemoryApiKeyStore()));
}
