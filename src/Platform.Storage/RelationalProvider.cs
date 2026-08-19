namespace Platform.Storage;

/// <summary>
/// Identifies which relational (EF Core) storage engine is configured via
/// "Storage:Provider". This is a provider-neutral enum - it describes which
/// engine is configured without Platform.Storage taking a dependency on
/// Platform.Storage.Relational (or any EF Core provider package).
/// </summary>
public enum RelationalProvider
{
    Sqlite,
    MySql,
    PostgreSql
}
