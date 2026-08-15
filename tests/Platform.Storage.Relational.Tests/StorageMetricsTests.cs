using System.Diagnostics.Metrics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Platform.Storage;
using Xunit;

namespace Platform.Storage.Relational.Tests;

/// <summary>
/// Proves the storage layer's self-observability instrumentation
/// (StorageMetrics, Meter "Platform.Storage") emits measurements via the
/// standard System.Diagnostics.Metrics.MeterListener API when a real
/// RelationalMetricSampleStore operation runs.
/// </summary>
public sealed class StorageMetricsTests
{
    [Fact]
    public async Task AddAsync_RecordsOperationLatencyHistogram()
    {
        var path = Path.Combine(Path.GetTempPath(), $"platform-storage-metrics-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        var options = new DbContextOptionsBuilder<PlatformDbContext>().UseSqlite(connectionString).Options;
        var factory = new TestDbContextFactory(options);
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
        }

        try
        {
            var metrics = new StorageMetrics();
            var store = new RelationalMetricSampleStore(factory, metrics);

            double? latencyMs = null;
            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == StorageMetrics.MeterName)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
            {
                if (instrument.Name == "platform.storage.operation.latency")
                {
                    latencyMs = measurement;
                }
            });
            listener.Start();

            await store.AddAsync(new MetricSample
            {
                Id = Guid.NewGuid(),
                TenantId = Guid.NewGuid(),
                ServiceName = "svc",
                Environment = "prod",
                MetricName = "cpu.usage",
                Value = 1,
                Timestamp = DateTimeOffset.UtcNow
            });

            listener.Dispose();

            Assert.True(latencyMs.HasValue, "Expected the storage operation latency histogram to record a measurement.");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch
                {
                    // best-effort cleanup
                }
            }
        }
    }

    [Fact]
    public async Task DeleteOlderThanAsync_IncrementsFailureCounter_WhenUnderlyingOperationFails()
    {
        // Dispose the factory's underlying connection scenario is awkward to
        // force through EF Core directly, so instead we point at a
        // connection string with no schema created - ExecuteDeleteAsync
        // against a missing table throws, proving the failure path.
        var path = Path.Combine(Path.GetTempPath(), $"platform-storage-metrics-fail-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        var options = new DbContextOptionsBuilder<PlatformDbContext>().UseSqlite(connectionString).Options;
        var factory = new TestDbContextFactory(options);
        // Intentionally do NOT call EnsureCreatedAsync, so the table is missing.

        try
        {
            var metrics = new StorageMetrics();
            var store = new RelationalMetricSampleStore(factory, metrics);

            long failureCount = 0;
            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == StorageMetrics.MeterName)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
            {
                if (instrument.Name == "platform.storage.failures")
                {
                    failureCount += measurement;
                }
            });
            listener.Start();

            await Assert.ThrowsAnyAsync<Exception>(() =>
                store.DeleteOlderThanAsync(DateTimeOffset.UtcNow, CancellationToken.None));

            listener.Dispose();

            Assert.True(failureCount >= 1, "Expected the storage failure counter to be incremented.");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch
                {
                    // best-effort cleanup
                }
            }
        }
    }

    private sealed class TestDbContextFactory : IDbContextFactory<PlatformDbContext>
    {
        private readonly DbContextOptions<PlatformDbContext> _options;

        public TestDbContextFactory(DbContextOptions<PlatformDbContext> options)
        {
            _options = options;
        }

        public PlatformDbContext CreateDbContext() => new(_options);

        public Task<PlatformDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
