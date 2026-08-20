using Microsoft.EntityFrameworkCore;

namespace Platform.Storage.Relational;

public sealed class RelationalMetricSampleStore : IMetricSampleStore
{
    private readonly IDbContextFactory<PlatformDbContext> _dbFactory;
    private readonly StorageMetrics _metrics;

    public RelationalMetricSampleStore(IDbContextFactory<PlatformDbContext> dbFactory, StorageMetrics? metrics = null)
    {
        _dbFactory = dbFactory;
        _metrics = metrics ?? new StorageMetrics();
    }

    public async Task AddAsync(MetricSample sample, CancellationToken cancellationToken = default)
    {
        await MeasureAsync(async () =>
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            db.MetricSamples.Add(new MetricSampleEntity
            {
                Id = sample.Id,
                TenantId = sample.TenantId,
                ServiceName = sample.ServiceName,
                Environment = sample.Environment,
                MetricName = sample.MetricName,
                RequestType = sample.RequestType,
                Value = sample.Value,
                TimestampUtc = TimeMapping.ToUtcDateTime(sample.Timestamp)
            });
            await db.SaveChangesAsync(cancellationToken);
        });
    }

    public Task<IReadOnlyList<MetricSample>> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        QueryAsync(tenantId, from: null, to: null, cancellationToken);

    public Task<IReadOnlyList<MetricSample>> QueryAsync(
        Guid tenantId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        return MeasureAsync(async () =>
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
            return (IReadOnlyList<MetricSample>)rows.Select(Map).ToList();
        });
    }

    public Task<IReadOnlyList<MetricSample>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return MeasureAsync(async () =>
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var rows = await db.MetricSamples.AsNoTracking().ToListAsync(cancellationToken);
            return (IReadOnlyList<MetricSample>)rows.Select(Map).ToList();
        });
    }

    public async Task DeleteOlderThanAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default)
    {
        await MeasureAsync(async () =>
        {
            var thresholdUtc = TimeMapping.ToUtcDateTime(threshold);
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

            // Server-side bulk delete: only rows strictly older than the threshold
            // are touched, so AggregateRollup rows (a separate table) and samples
            // from any tenant newer than the threshold are never affected.
            await db.MetricSamples
                .Where(s => s.TimestampUtc < thresholdUtc)
                .ExecuteDeleteAsync(cancellationToken);
        });
    }

    private async Task MeasureAsync(Func<Task> operation)
    {
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            await operation();
        }
        catch
        {
            _metrics.Failures.Add(1);
            throw;
        }
        finally
        {
            _metrics.OperationLatencyMs.Record((DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
        }
    }

    private async Task<T> MeasureAsync<T>(Func<Task<T>> operation)
    {
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            return await operation();
        }
        catch
        {
            _metrics.Failures.Add(1);
            throw;
        }
        finally
        {
            _metrics.OperationLatencyMs.Record((DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
        }
    }

    private static MetricSample Map(MetricSampleEntity e) => new()
    {
        Id = e.Id,
        TenantId = e.TenantId,
        ServiceName = e.ServiceName,
        Environment = e.Environment,
        MetricName = e.MetricName,
        RequestType = e.RequestType,
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
        var rollupList = rollups is IReadOnlyCollection<AggregateRollup> collection ? collection : rollups.ToList();
        if (rollupList.Count == 0)
        {
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        // Every RunOnceAsync pass recomputes buckets from all raw samples still
        // in the retention window, so the same (tenant, service, metric,
        // request type, bucket) tuple is recomputed on every run. Without this
        // upsert, each run would blindly INSERT a brand-new duplicate row for
        // buckets that were already written by a previous run, causing
        // unbounded row growth the longer the aggregation job stays running.
        var tenantIds = rollupList.Select(r => r.TenantId).Distinct().ToList();
        var bucketStarts = rollupList.Select(r => TimeMapping.ToUtcDateTime(r.BucketStart)).Distinct().ToList();

        var existingRows = await db.AggregateRollups
            .Where(e => tenantIds.Contains(e.TenantId) && bucketStarts.Contains(e.BucketStartUtc))
            .ToListAsync(cancellationToken);

        var existingByKey = existingRows.ToDictionary(RollupKey.FromEntity);

        foreach (var rollup in rollupList)
        {
            var serviceId = ServiceIdGenerator.Compute(rollup.TenantId, rollup.ServiceName, rollup.Environment);
            var bucketStartUtc = TimeMapping.ToUtcDateTime(rollup.BucketStart);
            var key = new RollupKey(rollup.TenantId, serviceId, rollup.MetricName, rollup.RequestType, bucketStartUtc);

            if (existingByKey.TryGetValue(key, out var existing))
            {
                existing.BucketEndUtc = TimeMapping.ToUtcDateTime(rollup.BucketEnd);
                existing.Count = rollup.Count;
                existing.Min = rollup.Min;
                existing.Max = rollup.Max;
                existing.Avg = rollup.Avg;
                existing.P50 = rollup.P50;
                existing.P95 = rollup.P95;
                existing.P99 = rollup.P99;
            }
            else
            {
                var entity = new AggregateRollupEntity
                {
                    Id = rollup.Id,
                    TenantId = rollup.TenantId,
                    ServiceId = serviceId,
                    ServiceName = rollup.ServiceName,
                    Environment = rollup.Environment,
                    MetricName = rollup.MetricName,
                    RequestType = rollup.RequestType,
                    BucketStartUtc = bucketStartUtc,
                    BucketEndUtc = TimeMapping.ToUtcDateTime(rollup.BucketEnd),
                    Count = rollup.Count,
                    Min = rollup.Min,
                    Max = rollup.Max,
                    Avg = rollup.Avg,
                    P50 = rollup.P50,
                    P95 = rollup.P95,
                    P99 = rollup.P99
                };
                db.AggregateRollups.Add(entity);
                existingByKey[key] = entity;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private readonly record struct RollupKey(Guid TenantId, Guid ServiceId, string MetricName, string? RequestType, DateTime BucketStartUtc)
    {
        public static RollupKey FromEntity(AggregateRollupEntity e) =>
            new(e.TenantId, e.ServiceId, e.MetricName, e.RequestType, e.BucketStartUtc);
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
            RequestType = e.RequestType,
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

public sealed class RelationalTenantMembershipStore : ITenantMembershipStore
{
    private readonly IDbContextFactory<PlatformDbContext> _dbFactory;

    public RelationalTenantMembershipStore(IDbContextFactory<PlatformDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task AddAsync(TenantMembership membership, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        db.TenantMemberships.Add(new TenantMembershipEntity
        {
            Id = membership.Id,
            TenantId = membership.TenantId,
            IdentityIssuer = membership.IdentityIssuer,
            IdentitySubject = membership.IdentitySubject,
            Role = membership.Role.ToString(),
            CreatedAtUtc = TimeMapping.ToUtcDateTime(membership.CreatedAt)
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            throw new InvalidOperationException(
                "Membership already exists for this tenant and identity.", ex);
        }
    }

    public async Task<TenantMembership?> GetByIdAsync(Guid membershipId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.TenantMemberships.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == membershipId, cancellationToken);
        return entity is null ? null : Map(entity);
    }

    public async Task<TenantMembership?> GetAsync(
        Guid tenantId,
        string identityIssuer,
        string identitySubject,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.TenantMemberships.AsNoTracking()
            .FirstOrDefaultAsync(
                m => m.TenantId == tenantId
                     && m.IdentityIssuer == identityIssuer
                     && m.IdentitySubject == identitySubject,
                cancellationToken);
        return entity is null ? null : Map(entity);
    }

    public async Task<IReadOnlyList<TenantMembership>> ListByTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.TenantMemberships.AsNoTracking()
            .Where(m => m.TenantId == tenantId)
            .OrderBy(m => m.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        return rows.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<TenantMembership>> ListByIdentityAsync(
        string identityIssuer,
        string identitySubject,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.TenantMemberships.AsNoTracking()
            .Where(m => m.IdentityIssuer == identityIssuer && m.IdentitySubject == identitySubject)
            .OrderBy(m => m.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        return rows.Select(Map).ToList();
    }

    private static TenantMembership Map(TenantMembershipEntity e) => new()
    {
        Id = e.Id,
        TenantId = e.TenantId,
        IdentityIssuer = e.IdentityIssuer,
        IdentitySubject = e.IdentitySubject,
        Role = Enum.TryParse<TenantMembershipRole>(e.Role, ignoreCase: true, out var role)
            ? role
            : TenantMembershipRole.Member,
        CreatedAt = TimeMapping.ToOffset(e.CreatedAtUtc)
    };

    internal static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        // Provider-neutral: SQLite / Postgres / MySQL unique violations surface
        // as DbUpdateException; message/inner text is the portable signal without
        // taking a hard dependency on each provider's exception type here.
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            var message = current.Message;
            if (message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
                || message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase)
                || message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
                || message.Contains("1062", StringComparison.Ordinal)) // MySQL ER_DUP_ENTRY
            {
                return true;
            }
        }

        return false;
    }
}

public sealed class RelationalTenantInvitationStore : ITenantInvitationStore
{
    private readonly IDbContextFactory<PlatformDbContext> _dbFactory;

    public RelationalTenantInvitationStore(IDbContextFactory<PlatformDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task AddAsync(TenantInvitation invitation, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        db.TenantInvitations.Add(ToEntity(invitation));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (RelationalTenantMembershipStore.IsUniqueConstraintViolation(ex))
        {
            throw new InvalidOperationException(
                "A pending invitation already exists for this tenant and email.", ex);
        }
    }

    public async Task<TenantInvitation?> GetByIdAsync(Guid invitationId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.TenantInvitations.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == invitationId, cancellationToken);
        return entity is null ? null : Map(entity);
    }

    public async Task<TenantInvitation?> GetPendingAsync(
        Guid tenantId,
        string normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.TenantInvitations.AsNoTracking()
            .FirstOrDefaultAsync(
                i => i.TenantId == tenantId
                     && i.NormalizedEmail == normalizedEmail
                     && i.Status == nameof(TenantInvitationStatus.Pending),
                cancellationToken);
        return entity is null ? null : Map(entity);
    }

    public async Task<IReadOnlyList<TenantInvitation>> ListByTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.TenantInvitations.AsNoTracking()
            .Where(i => i.TenantId == tenantId)
            .OrderByDescending(i => i.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        return rows.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<TenantInvitation>> ListPendingByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.TenantInvitations.AsNoTracking()
            .Where(i =>
                i.NormalizedEmail == normalizedEmail
                && i.Status == nameof(TenantInvitationStatus.Pending))
            .OrderBy(i => i.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        return rows.Select(Map).ToList();
    }

    public async Task UpdateAsync(TenantInvitation invitation, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.TenantInvitations
            .FirstOrDefaultAsync(i => i.Id == invitation.Id, cancellationToken);
        if (entity is null)
        {
            throw new InvalidOperationException($"Invitation '{invitation.Id}' was not found.");
        }

        entity.TenantId = invitation.TenantId;
        entity.NormalizedEmail = invitation.NormalizedEmail;
        entity.Status = invitation.Status.ToString();
        entity.CreatedByIssuer = invitation.CreatedByIssuer;
        entity.CreatedBySubject = invitation.CreatedBySubject;
        entity.CreatedAtUtc = TimeMapping.ToUtcDateTime(invitation.CreatedAt);
        entity.ClaimedAtUtc = invitation.ClaimedAt is null
            ? null
            : TimeMapping.ToUtcDateTime(invitation.ClaimedAt.Value);
        entity.ClaimedByIssuer = invitation.ClaimedByIssuer;
        entity.ClaimedBySubject = invitation.ClaimedBySubject;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static TenantInvitationEntity ToEntity(TenantInvitation invitation) => new()
    {
        Id = invitation.Id,
        TenantId = invitation.TenantId,
        NormalizedEmail = invitation.NormalizedEmail,
        Status = invitation.Status.ToString(),
        CreatedByIssuer = invitation.CreatedByIssuer,
        CreatedBySubject = invitation.CreatedBySubject,
        CreatedAtUtc = TimeMapping.ToUtcDateTime(invitation.CreatedAt),
        ClaimedAtUtc = invitation.ClaimedAt is null
            ? null
            : TimeMapping.ToUtcDateTime(invitation.ClaimedAt.Value),
        ClaimedByIssuer = invitation.ClaimedByIssuer,
        ClaimedBySubject = invitation.ClaimedBySubject
    };

    private static TenantInvitation Map(TenantInvitationEntity e) => new()
    {
        Id = e.Id,
        TenantId = e.TenantId,
        NormalizedEmail = e.NormalizedEmail,
        Status = Enum.TryParse<TenantInvitationStatus>(e.Status, ignoreCase: true, out var status)
            ? status
            : TenantInvitationStatus.Pending,
        CreatedByIssuer = e.CreatedByIssuer,
        CreatedBySubject = e.CreatedBySubject,
        CreatedAt = TimeMapping.ToOffset(e.CreatedAtUtc),
        ClaimedAt = e.ClaimedAtUtc is null ? null : TimeMapping.ToOffset(e.ClaimedAtUtc.Value),
        ClaimedByIssuer = e.ClaimedByIssuer,
        ClaimedBySubject = e.ClaimedBySubject
    };
}
