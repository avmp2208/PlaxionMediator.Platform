using Microsoft.EntityFrameworkCore;

namespace Platform.Storage.Relational;

/// <summary>
/// EF Core DbContext shared by the SQLite, MySQL and PostgreSQL providers. EF
/// types stay inside this project; callers only see Platform.Storage
/// interfaces/models.
/// </summary>
public sealed class PlatformDbContext : DbContext
{
    public PlatformDbContext(DbContextOptions<PlatformDbContext> options)
        : base(options)
    {
    }

    internal DbSet<MetricSampleEntity> MetricSamples => Set<MetricSampleEntity>();
    internal DbSet<AggregateRollupEntity> AggregateRollups => Set<AggregateRollupEntity>();
    internal DbSet<ServiceEntity> Services => Set<ServiceEntity>();
    internal DbSet<ApiKeyEntity> ApiKeys => Set<ApiKeyEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MetricSampleEntity>(e =>
        {
            e.ToTable("metric_samples");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.TimestampUtc });
            e.HasIndex(x => new { x.TenantId, x.ServiceName, x.MetricName, x.RequestType });
            e.Property(x => x.ServiceName).HasMaxLength(256).IsRequired();
            e.Property(x => x.Environment).HasMaxLength(128).IsRequired();
            e.Property(x => x.MetricName).HasMaxLength(256).IsRequired();
            e.Property(x => x.RequestType).HasMaxLength(256);
        });

        modelBuilder.Entity<AggregateRollupEntity>(e =>
        {
            e.ToTable("aggregate_rollups");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.ServiceId, x.BucketStartUtc, x.RequestType });
            e.Property(x => x.ServiceName).HasMaxLength(256).IsRequired();
            e.Property(x => x.Environment).HasMaxLength(128).IsRequired();
            e.Property(x => x.MetricName).HasMaxLength(256).IsRequired();
            e.Property(x => x.RequestType).HasMaxLength(256);
        });

        modelBuilder.Entity<ServiceEntity>(e =>
        {
            e.ToTable("services");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.Name, x.Environment }).IsUnique();
            e.Property(x => x.Name).HasMaxLength(256).IsRequired();
            e.Property(x => x.Environment).HasMaxLength(128).IsRequired();
        });

        modelBuilder.Entity<ApiKeyEntity>(e =>
        {
            e.ToTable("api_keys");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TenantId);
            e.HasIndex(x => x.HashedKey);
            e.Property(x => x.Name).HasMaxLength(256).IsRequired();
            e.Property(x => x.HashedKey).HasMaxLength(128).IsRequired();
        });
    }
}
