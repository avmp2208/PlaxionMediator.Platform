namespace Platform.Hosting;

/// <summary>
/// Parses host process arguments for the explicit database migration runner.
/// Usage: <c>dotnet Platform.Api.dll --migrate</c> (applies migrations and exits).
/// </summary>
public static class MigrateCommand
{
    public const string ArgumentName = "--migrate";

    public static bool IsMigrateOnly(string[] args) =>
        args.Any(a => string.Equals(a, ArgumentName, StringComparison.OrdinalIgnoreCase));
}
