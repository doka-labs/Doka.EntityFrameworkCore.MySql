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

    [Fact]
    public void WithConnectionString_clears_DataSource_and_Connection()
    {
        using var dataSource = new MySqlDataSourceBuilder(ConnectionStringA).Build();
        using var connection = new MySqlConnection(ConnectionStringA);
        var extension = new MySqlOptionsExtension()
            .WithDataSource(dataSource)
            .WithConnectionString(ConnectionStringB);

        Assert.Equal(ConnectionStringB, extension.ConnectionString);
        Assert.Null(extension.DataSource);
        Assert.Null(extension.Connection);
    }

    [Fact]
    public void WithConnection_clears_DataSource_and_ConnectionString()
    {
        using var dataSource = new MySqlDataSourceBuilder(ConnectionStringA).Build();
        using var connection = new MySqlConnection(ConnectionStringB);
        var extension = new MySqlOptionsExtension()
            .WithDataSource(dataSource)
            .WithConnection(connection);

        Assert.Same(connection, extension.Connection);
        Assert.Null(extension.DataSource);
        Assert.Null(extension.ConnectionString);
    }

    [Fact]
    public void WithDataSource_clears_ConnectionString_and_Connection()
    {
        using var connection = new MySqlConnection(ConnectionStringA);
        using var dataSource = new MySqlDataSourceBuilder(ConnectionStringB).Build();
        var extension = new MySqlOptionsExtension()
            .WithConnection(connection)
            .WithDataSource(dataSource);

        Assert.Same(dataSource, extension.DataSource);
        Assert.Null(extension.ConnectionString);
        Assert.Null(extension.Connection);
    }

    [Fact]
    public void WithServerVersion_preserves_all_other_properties()
    {
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));
        var retryOptions = MySqlRetryOptions.Create(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(2));
        var extension = new MySqlOptionsExtension()
            .WithConnectionString(ConnectionStringA)
            .WithServerVersion(serverVersion)
            .WithRetryOptions(retryOptions)
            .WithDefaultGuidFormat(MySqlGuidFormat.Char36);

        var newVersion = MySqlServerVersion.MariaDb(new Version(11, 8, 0));
        var rotated = extension.WithServerVersion(newVersion);

        Assert.Same(newVersion, rotated.ServerVersion);
        Assert.Equal(ConnectionStringA, rotated.ConnectionString);
        Assert.Same(retryOptions, rotated.RetryOptions);
        Assert.Equal(MySqlGuidFormat.Char36, rotated.DefaultGuidFormat);
    }

    [Fact]
    public void Connection_path_clones_preserve_the_user_variable_requirement()
    {
        var seeded = new MySqlOptionsExtension()
            .WithUserVariablesRequired()
            .WithConnectionString(ConnectionStringA);

        var rotated = seeded.WithConnectionString(ConnectionStringB);

        Assert.True(rotated.UserVariablesRequired);
        Assert.Equal(ConnectionStringB, rotated.ConnectionString);
    }

    [Fact]
    public void Idempotent_WithA_WithB_WithA_matches_WithA_WithB()
    {
        var versionA = MySqlServerVersion.MySql(new Version(8, 4, 0));
        var versionB = MySqlServerVersion.MariaDb(new Version(11, 8, 0));
        var seeded = new MySqlOptionsExtension().WithConnectionString(ConnectionStringA);

        var twoStep = seeded.WithServerVersion(versionA).WithDefaultGuidFormat(MySqlGuidFormat.Char36);
        var threeStep = seeded
            .WithServerVersion(versionB)
            .WithDefaultGuidFormat(MySqlGuidFormat.Char36)
            .WithServerVersion(versionA);

        Assert.Same(twoStep.ServerVersion, threeStep.ServerVersion);
        Assert.Equal(twoStep.DefaultGuidFormat, threeStep.DefaultGuidFormat);
        Assert.Equal(twoStep.ConnectionString, threeStep.ConnectionString);
        Assert.Equal(twoStep.DataSource, threeStep.DataSource);
        Assert.Equal(twoStep.Connection, threeStep.Connection);
    }

    [Fact]
    public void Relational_options_survive_a_DataSource_switch()
    {
        using var dataSource = new MySqlDataSourceBuilder(ConnectionStringA).Build();
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));
        var seeded = new MySqlOptionsExtension()
            .WithConnectionString(ConnectionStringA)
            .WithServerVersion(serverVersion);

        var withTimeout = (MySqlOptionsExtension)seeded.WithCommandTimeout(42);

        var rotated = withTimeout.WithDataSource(dataSource);

        Assert.Equal(42, rotated.CommandTimeout);
        Assert.Same(serverVersion, rotated.ServerVersion);
        Assert.Same(dataSource, rotated.DataSource);
        Assert.Null(rotated.ConnectionString);
        Assert.Null(rotated.Connection);
    }

    [Fact]
    public void WithDefaultGuidFormat_rejects_undefined_enum_value()
    {
        var extension = new MySqlOptionsExtension();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => extension.WithDefaultGuidFormat((MySqlGuidFormat)42));
    }
}
