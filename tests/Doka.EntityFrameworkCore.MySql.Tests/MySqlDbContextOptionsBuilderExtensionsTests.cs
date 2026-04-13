namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Covers option-extension registration and mutation behavior.
/// </summary>
public sealed class MySqlDbContextOptionsBuilderExtensionsTests
{
    /// <summary>
    /// Verifies that <c>UseMySql(...)</c> stores the basic provider options.
    /// </summary>
    [Fact]
    public void UseMySql_stores_connection_string_and_server_version()
    {
        var builder = new DbContextOptionsBuilder();
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));

        builder.UseMySql("Server=localhost;Database=doka;User ID=root;Password=password;", serverVersion);

        var extension = Assert.IsType<MySqlOptionsExtension>(builder.Options.FindExtension<MySqlOptionsExtension>());

        Assert.Equal("Server=localhost;Database=doka;User ID=root;Password=password;", extension.ConnectionString);
        Assert.Equal(serverVersion, extension.ServerVersion);
        Assert.Null(extension.RetryOptions);
        Assert.Equal(MySqlGuidFormat.Binary16, extension.DefaultGuidFormat);
    }

    /// <summary>
    /// Verifies that the approved provider-specific options mutate the existing extension snapshot.
    /// </summary>
    [Fact]
    public void Approved_provider_options_update_the_existing_extension()
    {
        var builder = new DbContextOptionsBuilder();
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));

        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            serverVersion,
            options => options
                .EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(12))
                .DefaultGuidFormat(MySqlGuidFormat.Char36));

        var extension = Assert.IsType<MySqlOptionsExtension>(builder.Options.FindExtension<MySqlOptionsExtension>());

        Assert.NotNull(extension.RetryOptions);
        Assert.Equal(3, extension.RetryOptions.MaxRetryCount);
        Assert.Equal(TimeSpan.FromSeconds(12), extension.RetryOptions.MaxRetryDelay);
        Assert.Equal(MySqlGuidFormat.Char36, extension.DefaultGuidFormat);
    }

    /// <summary>
    /// Verifies that the data-source overload stores the caller-owned data source.
    /// </summary>
    [Fact]
    public void UseMySql_stores_data_source_and_server_version()
    {
        using var dataSource = new MySqlDataSourceBuilder(
            "Server=localhost;Database=doka;User ID=root;Password=password;").Build();
        var builder = new DbContextOptionsBuilder();
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));

        builder.UseMySql(dataSource, serverVersion);

        var extension = Assert.IsType<MySqlOptionsExtension>(builder.Options.FindExtension<MySqlOptionsExtension>());

        Assert.Same(dataSource, extension.DataSource);
        Assert.Null(extension.ConnectionString);
        Assert.Null(extension.Connection);
        Assert.Equal(serverVersion, extension.ServerVersion);
    }

    /// <summary>
    /// Verifies that retry configuration validates invalid arguments.
    /// </summary>
    [Fact]
    public void EnableRetryOnFailure_rejects_invalid_arguments()
    {
        var builder = new DbContextOptionsBuilder();
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            serverVersion,
            options => options.EnableRetryOnFailure(maxRetryCount: 0)));

        Assert.Equal("maxRetryCount", exception.ParamName);
    }
}
