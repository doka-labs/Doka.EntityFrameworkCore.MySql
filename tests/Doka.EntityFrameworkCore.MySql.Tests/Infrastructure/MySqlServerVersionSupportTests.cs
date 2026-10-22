namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies provider-option enforcement for supported and unsupported server
/// release lines.
/// </summary>
public sealed class MySqlServerVersionSupportTests
{
    private const string ConnectionString = "Server=localhost;Database=doka;User ID=root;Password=password;";

    /// <summary>
    /// Ensures supported release lines pass option validation without an opt-in.
    /// </summary>
    [Theory]
    [InlineData(false, 8, 4)]
    [InlineData(false, 9, 7)]
    [InlineData(true, 10, 11)]
    [InlineData(true, 11, 4)]
    [InlineData(true, 11, 8)]
    [InlineData(true, 12, 3)]
    public void Supported_release_lines_pass_default_validation(
        bool isMariaDb,
        int major,
        int minor
    )
    {
        // Arrange
        var serverVersion = CreateServerVersion(
            isMariaDb,
            new Version(major, minor, 0),
            MySqlServerVersionCompatibilityMode.SupportedOnly);

        var (extension, options) = CreateOptions(serverVersion);

        // Act
        var exception = Record.Exception(() => extension.Validate(options));

        // Assert
        Assert.Null(exception);
    }

    /// <summary>
    /// Ensures every unsupported classification fails option validation without
    /// an explicit compatibility opt-in.
    /// </summary>
    [Theory]
    [InlineData(false, 8, 0, MySqlServerVersionSupportStatus.Legacy)]
    [InlineData(false, 9, 6, MySqlServerVersionSupportStatus.Unvalidated)]
    [InlineData(false, 9, 8, MySqlServerVersionSupportStatus.Future)]
    [InlineData(true, 10, 6, MySqlServerVersionSupportStatus.Legacy)]
    [InlineData(true, 11, 6, MySqlServerVersionSupportStatus.Unvalidated)]
    [InlineData(true, 12, 4, MySqlServerVersionSupportStatus.Future)]
    public void Unsupported_release_lines_fail_default_validation(
        bool isMariaDb,
        int major,
        int minor,
        MySqlServerVersionSupportStatus expectedStatus
    )
    {
        // Arrange
        var serverVersion = CreateServerVersion(
            isMariaDb,
            new Version(major, minor, 0),
            MySqlServerVersionCompatibilityMode.SupportedOnly);

        var (extension, options) = CreateOptions(serverVersion);
        Action act = () => extension.Validate(options);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<NotSupportedException>(recordedException);
        Assert.Equal(expectedStatus, serverVersion.SupportStatus);
        Assert.Contains(nameof(MySqlServerVersionCompatibilityMode.AllowUnsupported), exception.Message);
        Assert.Contains(ServerVersionSupportPolicy.SupportedMatrix, exception.Message);
    }

    /// <summary>
    /// Ensures the explicit compatibility opt-in permits every unsupported
    /// classification.
    /// </summary>
    [Theory]
    [InlineData(false, 8, 0)]
    [InlineData(false, 9, 6)]
    [InlineData(false, 9, 8)]
    [InlineData(true, 10, 6)]
    [InlineData(true, 11, 6)]
    [InlineData(true, 12, 4)]
    public void Explicit_compatibility_mode_allows_unsupported_release_lines(
        bool isMariaDb,
        int major,
        int minor
    )
    {
        // Arrange
        var serverVersion = CreateServerVersion(
            isMariaDb,
            new Version(major, minor, 0),
            MySqlServerVersionCompatibilityMode.AllowUnsupported);

        var (extension, options) = CreateOptions(serverVersion);

        // Act
        var exception = Record.Exception(() => extension.Validate(options));

        // Assert
        Assert.Null(exception);
    }

    private static MySqlServerVersion CreateServerVersion(
        bool isMariaDb,
        Version version,
        MySqlServerVersionCompatibilityMode compatibilityMode
    ) => isMariaDb
        ? MySqlServerVersion.MariaDb(version, compatibilityMode)
        : MySqlServerVersion.MySql(version, compatibilityMode);

    private static (MySqlOptionsExtension Extension, DbContextOptions Options) CreateOptions(
        MySqlServerVersion serverVersion
    )
    {
        var builder = new DbContextOptionsBuilder();
        builder.UseMySql(ConnectionString, serverVersion);

        return (Assert.IsType<MySqlOptionsExtension>(builder.Options.FindExtension<MySqlOptionsExtension>()),
            builder.Options);
    }
}
