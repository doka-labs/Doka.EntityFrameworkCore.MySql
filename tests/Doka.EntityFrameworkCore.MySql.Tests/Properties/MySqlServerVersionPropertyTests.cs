using FsCheck.Xunit;

namespace Doka.EntityFrameworkCore.MySql.Tests.Properties;

/// <summary>
/// Property-style coverage for <see cref="MySqlServerVersion.Parse(string)"/>.
/// Three complementary surfaces are pinned:
/// (1) Representative real-world <c>@@version</c> strings from supported and
///     legacy MySQL and MariaDB deployments must parse to the expected version
///     and engine.
/// (2) For any randomized non-null input the parser either succeeds or throws an
///     <see cref="ArgumentException"/> family member; no other exception type may escape.
/// (3) A version-token prefixed with non-digit text and suffixed with a non-digit-non-dot
///     character still parses to the leading numeric token; this property pins the
///     "find the first digit, consume until non-digit-non-dot" parser contract.
/// </summary>
public sealed class MySqlServerVersionPropertyTests
{
    [Theory]
    [InlineData("5.7.42", false, 5, 7, 42)]
    [InlineData("5.7.44-log", false, 5, 7, 44)]
    [InlineData("8.0.36", false, 8, 0, 36)]
    [InlineData("8.0.40-cluster", false, 8, 0, 40)]
    [InlineData("8.4.0", false, 8, 4, 0)]
    [InlineData("8.4.3", false, 8, 4, 3)]
    [InlineData("8.4.0-debug", false, 8, 4, 0)]
    [InlineData("9.7.2", false, 9, 7, 2)]
    [InlineData("5.5.5-10.4.31-MariaDB", true, 10, 4, 31)]
    [InlineData("5.5.5-10.11.18-MariaDB-ubu2204", true, 10, 11, 18)]
    [InlineData("10.4.31-MariaDB-1:10.4.31+maria~deb10", true, 10, 4, 31)]
    [InlineData("10.5.22-MariaDB-log", true, 10, 5, 22)]
    [InlineData("10.6.16-MariaDB", true, 10, 6, 16)]
    [InlineData("10.11.7-MariaDB", true, 10, 11, 7)]
    [InlineData("11.0.4-MariaDB", true, 11, 0, 4)]
    [InlineData("11.4.3-MariaDB", true, 11, 4, 3)]
    [InlineData("11.4.4-MariaDB-1:11.4.4+maria", true, 11, 4, 4)]
    [InlineData("11.8.1-MariaDB", true, 11, 8, 1)]
    [InlineData("11.8.8-MariaDB", true, 11, 8, 8)]
    [InlineData("12.3.2-MariaDB", true, 12, 3, 2)]
    [InlineData("8.0.36-0ubuntu0.22.04.1", false, 8, 0, 36)]
    [InlineData("8.0.36-commercial", false, 8, 0, 36)]
    [InlineData("8.4.0 community-edition", false, 8, 4, 0)]
    [InlineData("MySQL 8.4.0", false, 8, 4, 0)]
    public void Parse_handles_real_world_at_version_strings(
        string serverVersion,
        bool expectedIsMariaDb,
        int expectedMajor,
        int expectedMinor,
        int expectedBuild
    )
    {
        var parsed = MySqlServerVersion.Parse(serverVersion);

        Assert.Equal(expectedIsMariaDb, parsed.IsMariaDb);
        Assert.Equal(expectedMajor, parsed.Version.Major);
        Assert.Equal(expectedMinor, parsed.Version.Minor);
        Assert.Equal(expectedBuild, parsed.Version.Build);
    }

    [Property(MaxTest = 1000)]
    public bool Parse_either_succeeds_or_throws_documented_exception(
        string? raw
    )
    {
        if (raw is null)
        {
            return true;
        }

        try
        {
            _ = MySqlServerVersion.Parse(raw);
            return true;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    [Property(MaxTest = 1000)]
    public bool Parse_finds_leading_numeric_token_regardless_of_surrounding_text(
        byte major,
        byte minor,
        byte build
    )
    {
        var majorComponent = (int)major;
        var minorComponent = (int)minor;
        var buildComponent = (int)build;
        var versionToken = FormattableString.Invariant($"{majorComponent}.{minorComponent}.{buildComponent}");
        var input = $"prefix-text {versionToken} suffix-text";

        var parsed = MySqlServerVersion.Parse(input);

        return parsed.Version.Major == majorComponent
            && parsed.Version.Minor == minorComponent
            && parsed.Version.Build == buildComponent;
    }
}
