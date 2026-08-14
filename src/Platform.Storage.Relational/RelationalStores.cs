using Microsoft.EntityFrameworkCore;

namespace Platform.Storage.Relational;

public sealed class RelationalMetricSampleStore : IMetricSampleStore
{
    private readonly IDbContextFactory<PlatformDbContext> _dbFactory;

    public RelationalMetricSampleStore(IDbContextFactory<PlatformDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task AddAsync(MetricSample sample, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        db.MetricSamples.Add(new MetricSampleEntity
        {
            Id = sample.Id,
            TenantId = sample.TenantId,
            ServiceName = sample.ServiceName,
            Environment = sample.Environment,
            MetricName = sample.MetricName,
            Value = sample.Value,
            TimestampUtc = TimeMapping.ToUtcDateTime(sample.Timestamp)
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<IReadOnlyList<MetricSample>> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        QueryAsync(tenantId, from: null, to: null, cancellationToken);

    public async Task<IReadOnlyList<MetricSample>> QueryAsync(
        Guid tenantId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.MetricSamples.AsNoTracking().Where(s => s.TenantId == tenantId);
        if (from is not null)
        {
            var fromUtc = TimeMapping.ToUtcDateTime(from.Value);
            query = query.Where(s => s.TimestampUtc >= fromUtc);
        }

        if (to is not null)
        {
            var toUtc = TimeMapping.ToUtcDateTime(to.Value);
            query = query.Where(s => s.TimestampUtc <= toUtc);
        }

        var rows = await query.OrderBy(s => s.TimestampUtc).ToListAsync(cancellationToken);
        return rows.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<MetricSample>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.MetricSamples.AsNoTracking().ToListAsync(cancellationToken);
        return rows.Select(Map).ToList();
    }

    private static MetricSample Map(MetricSampleEntity e) => new()
    {
        Id = e.Id,
        TenantId = e.TenantId,
        ServiceName = e.ServiceName,
        Environment = e.Environment,
        MetricName = e.MetricName,
        Value = e.Value,
        Timestamp = TimeMapping.ToOffset(e.TimestampUtc)
    };
}

public sealed class RelationalAggregateRollupStore : IAggregateRollupStore
{
    private readonly IDbContextFactory<PlatformDbContext> _dbFactory;

    public RelationalAggregateRollupStore(IDbContextFactory<PlatformDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task AddRangeAsync(IEnumerable<AggregateRollup> rollups, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        foreach (var rollup in rollups)
        {
            db.AggregateRollups.Add(new AggregateRollupEntity
            {
                Id = rollup.Id,
                TenantId = rollup.TenantId,
                ServiceId = ServiceIdGenerator.Compute(rollup.TenantId, rollup.ServiceName, rollup.Environment),
                ServiceName = rollup.ServiceName,
                Environment = rollup.Environment,
                MetricName = rollup.MetricName,
                BucketStartUtc = TimeMapping.ToUtcDateTime(rollup.BucketStart),
                BucketEndUtc = TimeMapping.ToUtcDateTime(rollup.BucketEnd),
                Count = rollup.Count,
                Min = rollup.Min,
                Max = rollup.Max,
                Avg = rollup.Avg,
                P50 = rollup.P50,
                P95 = rollup.P95,
                P99 = rollup.P99
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AggregateRollup>> QueryAsync(
        Guid tenantId,
        Guid serviceId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var fromUtc = TimeMapping.ToUtcDateTime(from);
        var toUtc = TimeMapping.ToUtcDateTime(to);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.AggregateRollups.AsNoTracking()
            .Where(r => r.TenantId == tenantId
                        && r.ServiceId == serviceId
                        && r.BucketStartUtc >= fromUtc
                        && r.BucketStartUtc <= toUtc)
            .OrderBy(r => r.BucketStartUtc)
            .ToListAsync(cancellationToken);

        return rows.Select(e => new AggregateRollup
        {
            Id = e.Id,
            TenantId = e.TenantId,
            ServiceName = e.ServiceName,
            Environment = e.Environment,
            MetricName = e.MetricName,
            BucketStart = TimeMapping.ToOffset(e.BucketStartUtc),
            BucketEnd = TimeMapping.ToOffset(e.BucketEndUtc),
            Count = e.Count,
            Min = e.Min,
            Max = e.Max,
            Avg = e.Avg,
            P50 = e.P50,
            P95 = e.P95,
            P99 = e.P99
        }).ToList();
    }
}

public sealed class RelationalServiceCatalog : IServiceCatalog
{
    private readonly IDbContextFactory<PlatformDbContext> _dbFactory;

    public RelationalServiceCatalog(IDbContextFactory<PlatformDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<Service> GetOrCreateAsync(Guid tenantId, string name, string environment, CancellationToken cancellationToken = default)
    {
        var id = ServiceIdGenerator.Compute(tenantId, name, environment);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.Services.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (existing is not null)
        {
            return Map(existing);
        }

        var entity = new ServiceEntity
        {
            Id = id,
            TenantId = tenantId,
            Name = name,
            Environment = environment
        };

        try
        {
            db.Services.Add(entity);
            await db.SaveChangesAsync(cancellationToken);
            return Map(entity);
        }
        catch (DbUpdateException)
        {
            existing = await db.Services.AsNoTracking().FirstAsync(s => s.Id == id, cancellationToken);
            return Map(existing);
        }
    }

    public async Task<IReadOnlyList<Service>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Services.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .ToListAsync(cancellationToken);
        return rows.Select(Map).ToList();
    }

    public async Task<Service?> GetByIdAsync(Guid tenantId, Guid serviceId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Services.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == serviceId && s.TenantId == tenantId, cancellationToken);
        return entity is null ? null : Map(entity);
    }

    private static Service Map(ServiceEntity e) => new()
    {
        Id = e.Id,
        TenantId = e.TenantId,
        Name = e.Name,
        Environment = e.Environment
    };
}

public sealed class RelationalApiKeyStore : IApiKeyStore
{
    private readonly IDbContextFactory<PlatformDbContext> _dbFactory;

    public RelationalApiKeyStore(IDbContextFactory<PlatformDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task AddAsync(ApiKey apiKey, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        db.ApiKeys.Add(new ApiKeyEntity
        {
            Id = apiKey.Id,
            TenantId = apiKey.TenantId,
            Name = apiKey.Name,
            HashedKey = apiKey.HashedKey,
            CreatedAtUtc = TimeMapping.ToUtcDateTime(apiKey.CreatedAt),
            RevokedAtUtc = apiKey.RevokedAt is null ? null : TimeMapping.ToUtcDateTime(apiKey.RevokedAt.Value)
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ApiKey>> GetAllActiveAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.ApiKeys.AsNoTracking()
            .Where(k => k.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        return rows.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<ApiKey>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.ApiKeys.AsNoTracking()
            .Where(k => k.TenantId == tenantId)
            .OrderByDescending(k => k.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        return rows.Select(Map).ToList();
    }

    public async Task<ApiKey?> GetByIdAsync(Guid apiKeyId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.ApiKeys.AsNoTracking().FirstOrDefaultAsync(k => k.Id == apiKeyId, cancellationToken);
        return entity is null ? null : Map(entity);
    }

    public async Task RevokeAsync(Guid apiKeyId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == apiKeyId, cancellationToken);
        if (entity is null)
        {
            return;
        }

        entity.RevokedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static ApiKey Map(ApiKeyEntity e) => new()
    {
        Id = e.Id,
        TenantId = e.TenantId,
        Name = e.Name,
        HashedKey = e.HashedKey,
        CreatedAt = TimeMapping.ToOffset(e.CreatedAtUtc),
        RevokedAt = e.RevokedAtUtc is null ? null : TimeMapping.ToOffset(e.RevokedAtUtc.Value)
    };
}
