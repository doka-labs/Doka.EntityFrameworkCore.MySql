namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Pins the cloning contract of <see cref="MySqlOptionsExtension"/>: every
/// <c>With*</c> method routes through <c>Clone()</c> (the copy-constructor)
/// plus, for the three mutex connection paths, the
/// <c>ResetOtherConnectionPaths</c> helper that nulls the inactive paths.
/// The previous implementation had three separate cloning routes
/// (<c>base.With*</c> for ConnectionString/Connection, a hand-rolled
/// <c>CopyRelationalOptionsTo</c> for DataSource); this test set guards
/// against re-introducing that asymmetry.
/// </summary>
public sealed class MySqlOptionsExtensionCloningTests
{
    private const string ConnectionStringA =
        "Server=localhost;Database=a;User ID=root;Password=pw;GuidFormat=Binary16;";

    private const string ConnectionStringB =
        "Server=localhost;Database=b;User ID=root;Password=pw;GuidFormat=Binary16;";

    /// <summary>
    /// Verifies <c>WithConnectionString</c> clears datasource and connection.
    /// </summary>
    [Fact]
    public void WithConnectionString_clears_DataSource_and_Connection()
    {
        // Arrange
        using var dataSource = new MySqlDataSourceBuilder(ConnectionStringA).Build();
        using var connection = new MySqlConnection(ConnectionStringA);

        // Act
        var extension = new MySqlOptionsExtension()
            .WithDataSource(dataSource)
            .WithConnectionString(ConnectionStringB);

        // Assert
        Assert.Equal(ConnectionStringB, extension.ConnectionString);
        Assert.Null(extension.DataSource);
        Assert.Null(extension.Connection);
    }

    /// <summary>
    /// Verifies <c>WithConnection</c> clears datasource and connectionstring.
    /// </summary>
    [Fact]
    public void WithConnection_clears_DataSource_and_ConnectionString()
    {
        // Arrange
        using var dataSource = new MySqlDataSourceBuilder(ConnectionStringA).Build();
        using var connection = new MySqlConnection(ConnectionStringB);

        // Act
        var extension = new MySqlOptionsExtension()
            .WithDataSource(dataSource)
            .WithConnection(connection);

        // Assert
        Assert.Same(connection, extension.Connection);
        Assert.Null(extension.DataSource);
        Assert.Null(extension.ConnectionString);
    }

    /// <summary>
    /// Verifies <c>WithDataSource</c> clears connectionstring and connection.
    /// </summary>
    [Fact]
    public void WithDataSource_clears_ConnectionString_and_Connection()
    {
        // Arrange
        using var connection = new MySqlConnection(ConnectionStringA);
        using var dataSource = new MySqlDataSourceBuilder(ConnectionStringB).Build();

        // Act
        var extension = new MySqlOptionsExtension()
            .WithConnection(connection)
            .WithDataSource(dataSource);

        // Assert
        Assert.Same(dataSource, extension.DataSource);
        Assert.Null(extension.ConnectionString);
        Assert.Null(extension.Connection);
    }

    /// <summary>
    /// Verifies <c>WithServerVersion</c> preserves all other properties.
    /// </summary>
    [Fact]
    public void WithServerVersion_preserves_all_other_properties()
    {
        // Arrange
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));
        var retryOptions = MySqlRetryOptions.Create(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(2));
        var extension = new MySqlOptionsExtension()
            .WithConnectionString(ConnectionStringA)
            .WithServerVersion(serverVersion)
            .WithRetryOptions(retryOptions)
            .WithDefaultGuidFormat(MySqlGuidFormat.Char36);

        var newVersion = MySqlServerVersion.MariaDb(new Version(11, 8, 0));

        // Act
        var rotated = extension.WithServerVersion(newVersion);

        // Assert
        Assert.Same(newVersion, rotated.ServerVersion);
        Assert.Equal(ConnectionStringA, rotated.ConnectionString);
        Assert.Same(retryOptions, rotated.RetryOptions);
        Assert.Equal(MySqlGuidFormat.Char36, rotated.DefaultGuidFormat);
    }

    /// <summary>
    /// Verifies connection path clones preserve the user variable requirement.
    /// </summary>
    [Fact]
    public void Connection_path_clones_preserve_the_user_variable_requirement()
    {
        // Arrange
        var seeded = new MySqlOptionsExtension()
            .WithUserVariablesRequired()
            .WithConnectionString(ConnectionStringA);

        // Act
        var rotated = seeded.WithConnectionString(ConnectionStringB);

        // Assert
        Assert.True(rotated.UserVariablesRequired);
        Assert.Equal(ConnectionStringB, rotated.ConnectionString);
    }

    /// <summary>
    /// Verifies idempotent witha withb witha matches witha withb.
    /// </summary>
    [Fact]
    public void Idempotent_WithA_WithB_WithA_matches_WithA_WithB()
    {
        // Arrange
        var versionA = MySqlServerVersion.MySql(new Version(8, 4, 0));
        var versionB = MySqlServerVersion.MariaDb(new Version(11, 8, 0));
        var seeded = new MySqlOptionsExtension().WithConnectionString(ConnectionStringA);
        var twoStep = seeded.WithServerVersion(versionA).WithDefaultGuidFormat(MySqlGuidFormat.Char36);

        // Act
        var threeStep = seeded
            .WithServerVersion(versionB)
            .WithDefaultGuidFormat(MySqlGuidFormat.Char36)
            .WithServerVersion(versionA);

        // Assert
        Assert.Same(twoStep.ServerVersion, threeStep.ServerVersion);
        Assert.Equal(twoStep.DefaultGuidFormat, threeStep.DefaultGuidFormat);
        Assert.Equal(twoStep.ConnectionString, threeStep.ConnectionString);
        Assert.Equal(twoStep.DataSource, threeStep.DataSource);
        Assert.Equal(twoStep.Connection, threeStep.Connection);
    }

    /// <summary>
    /// Verifies relational options survive a datasource switch.
    /// </summary>
    [Fact]
    public void Relational_options_survive_a_DataSource_switch()
    {
        // Arrange
        using var dataSource = new MySqlDataSourceBuilder(ConnectionStringA).Build();
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));
        var seeded = new MySqlOptionsExtension()
            .WithConnectionString(ConnectionStringA)
            .WithServerVersion(serverVersion);

        var withTimeout = (MySqlOptionsExtension)seeded.WithCommandTimeout(42);

        // Act
        var rotated = withTimeout.WithDataSource(dataSource);

        // Assert
        Assert.Equal(42, rotated.CommandTimeout);
        Assert.Same(serverVersion, rotated.ServerVersion);
        Assert.Same(dataSource, rotated.DataSource);
        Assert.Null(rotated.ConnectionString);
        Assert.Null(rotated.Connection);
    }

    /// <summary>
    /// Verifies <c>WithDefaultGuidFormat</c> rejects undefined enum value.
    /// </summary>
    [Fact]
    public void WithDefaultGuidFormat_rejects_undefined_enum_value()
    {
        // Arrange
        var extension = new MySqlOptionsExtension();
        Action act = () => extension.WithDefaultGuidFormat((MySqlGuidFormat)42);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(recordedException);
    }
}
