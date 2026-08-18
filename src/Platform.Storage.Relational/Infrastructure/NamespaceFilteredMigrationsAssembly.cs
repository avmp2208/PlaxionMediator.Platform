using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Internal;

namespace Platform.Storage.Relational.Infrastructure;

/// <summary>
/// Restricts EF Core migration discovery to a single provider namespace so
/// Sqlite and PostgreSQL migrations can coexist in the same assembly.
/// </summary>
#pragma warning disable EF1001 // Internal EF Core API — intentional multi-provider filter
#pragma warning disable EF9100 // Obsolete extensions accessors — required for provider detection
public sealed class NamespaceFilteredMigrationsAssembly : MigrationsAssembly
{
    private readonly string _namespacePrefix;
    private IReadOnlyDictionary<string, TypeInfo>? _migrations;
    private ModelSnapshot? _modelSnapshot;
    private bool _snapshotResolved;

    public NamespaceFilteredMigrationsAssembly(
        ICurrentDbContext currentContext,
        IDbContextOptions options,
        IMigrationsIdGenerator idGenerator,
        IDiagnosticsLogger<DbLoggerCategory.Migrations> logger)
        : base(currentContext, options, idGenerator, logger)
    {
        _namespacePrefix = ResolveNamespace(options);
    }

    public override IReadOnlyDictionary<string, TypeInfo> Migrations =>
        _migrations ??= new Dictionary<string, TypeInfo>(
            base.Migrations.Where(pair =>
                pair.Value.Namespace is not null
                && pair.Value.Namespace.StartsWith(_namespacePrefix, StringComparison.Ordinal)));

    public override ModelSnapshot? ModelSnapshot
    {
        get
        {
            if (_snapshotResolved)
            {
                return _modelSnapshot;
            }

            _snapshotResolved = true;
            var snapshotType = Assembly
                .GetTypes()
                .FirstOrDefault(t =>
                    !t.IsAbstract
                    && typeof(ModelSnapshot).IsAssignableFrom(t)
                    && t.Namespace is not null
                    && t.Namespace.StartsWith(_namespacePrefix, StringComparison.Ordinal));

            _modelSnapshot = snapshotType is null
                ? null
                : (ModelSnapshot)Activator.CreateInstance(snapshotType)!;
            return _modelSnapshot;
        }
    }

    private static string ResolveNamespace(IDbContextOptions options)
    {
        foreach (var extension in options.Extensions)
        {
            var name = extension.GetType().FullName ?? extension.GetType().Name;
            if (name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                return "Platform.Storage.Relational.Migrations.Sqlite";
            }

            if (name.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Postgre", StringComparison.OrdinalIgnoreCase))
            {
                return "Platform.Storage.Relational.Migrations.PostgreSql";
            }
        }

        throw new InvalidOperationException(
            "Unable to resolve migrations namespace from DbContext options. " +
            "Expected Sqlite or Npgsql provider extension.");
    }
}
#pragma warning restore EF9100
#pragma warning restore EF1001
