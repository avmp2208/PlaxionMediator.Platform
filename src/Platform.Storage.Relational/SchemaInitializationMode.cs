namespace Platform.Storage.Relational;

/// <summary>
/// Controls how <see cref="RelationalSchemaInitializer"/> treats the database
/// schema at host startup.
/// </summary>
public enum SchemaInitializationMode
{
    /// <summary>Do nothing (caller is responsible for schema).</summary>
    None = 0,

    /// <summary>Apply EF Core migrations (MigrateAsync).</summary>
    Migrate = 1,

    /// <summary>
    /// Do not apply migrations; fail if any are pending. Used in Production
    /// so replicas never race to migrate — run a one-shot --migrate job first.
    /// </summary>
    ValidateOnly = 2,

    /// <summary>
    /// Legacy EnsureCreated path retained only for MySQL until a dedicated
    /// migrations assembly is added. Prefer Migrate for Sqlite/PostgreSQL.
    /// </summary>
    EnsureCreated = 3
}
