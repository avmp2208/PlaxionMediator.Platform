using Platform.Hosting;
using Platform.Storage;
using Xunit;

namespace Platform.Api.Tests;

/// <summary>
/// Unit tests for <see cref="PlatformStorageRegistration.ParseRelationalProvider"/>,
/// the string-to-<see cref="RelationalProvider"/> parsing logic used by
/// <see cref="PlatformStorageRegistration.AddPlatformStorage"/> to select the
/// durable EF Core storage provider.
/// </summary>
public class PlatformStorageRegistrationTests
{
    [Theory]
    [InlineData("Sqlite", RelationalProvider.Sqlite)]
    [InlineData("sqlite", RelationalProvider.Sqlite)]
    [InlineData("MySql", RelationalProvider.MySql)]
    [InlineData("mysql", RelationalProvider.MySql)]
    [InlineData("Postgres", RelationalProvider.PostgreSql)]
    [InlineData("postgres", RelationalProvider.PostgreSql)]
    [InlineData("PostgreSql", RelationalProvider.PostgreSql)]
    [InlineData("postgresql", RelationalProvider.PostgreSql)]
    public void ParseRelationalProvider_RecognizesKnownProviders(string value, RelationalProvider expected)
    {
        var result = PlatformStorageRegistration.ParseRelationalProvider(value);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("InMemory")]
    [InlineData("SomeUnknownProvider")]
    public void ParseRelationalProvider_ReturnsNull_ForUnsetOrUnrecognizedValues(string? value)
    {
        var result = PlatformStorageRegistration.ParseRelationalProvider(value);

        Assert.Null(result);
    }
}
