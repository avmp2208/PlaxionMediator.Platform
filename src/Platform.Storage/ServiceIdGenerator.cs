namespace Platform.Storage;

/// <summary>
/// Deterministic, content-derived service id so the catalog and the
/// aggregation job (which never share a shared "insert" transaction)
/// agree on the same Service.Id for a given tenant/name/environment.
/// </summary>
public static class ServiceIdGenerator
{
    public static Guid Compute(Guid tenantId, string name, string environment)
    {
        var key = $"{tenantId}:{name}:{environment}";
        var bytes = System.Text.Encoding.UTF8.GetBytes(key);
        var hash = System.Security.Cryptography.MD5.HashData(bytes);
        return new Guid(hash);
    }
}
