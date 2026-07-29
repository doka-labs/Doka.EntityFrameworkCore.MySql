namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Covers provider singleton-option initialization and validation behavior.
/// </summary>
public sealed class MySqlSingletonOptionsTests
{
    /// <summary>
    /// Verifies that the singleton options cache the resolved version, capabilities, and persisted Phase 1 settings.
    /// </summary>
    [Fact]
    public void Initialize_caches_resolved_server_version_capabilities_and_phase1_settings()
    {
        var builder = new DbContextOptionsBuilder();
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 8, 0));

        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            serverVersion,
            options => options
                .EnableRetryOnFailure(maxRetryCount: 4, maxRetryDelay: TimeSpan.FromSeconds(9))
                .DefaultGuidFormat(MySqlGuidFormat.Char36));

        var singletonOptions = new MySqlSingletonOptions();

        singletonOptions.Initialize(builder.Options);

        Assert.Equal(serverVersion, singletonOptions.ServerVersion);
        Assert.Equal(serverVersion.Profile, singletonOptions.Profile);
        Assert.NotNull(singletonOptions.RetryOptions);
        Assert.Equal(4, singletonOptions.RetryOptions.MaxRetryCount);
        Assert.Equal(TimeSpan.FromSeconds(9), singletonOptions.RetryOptions.MaxRetryDelay);
        Assert.Equal(MySqlGuidFormat.Char36, singletonOptions.DefaultGuidFormat);
        Assert.False(singletonOptions.UsesDataSource);
    }

    /// <summary>
    /// Verifies that validation fails when the server version changes for a shared service provider.
    /// </summary>
    [Fact]
    public void Validate_rejects_server_version_changes()
    {
        var originalOptions = CreateOptions(MySqlServerVersion.MySql(new Version(8, 4, 0)), MySqlGuidFormat.Binary16);
        var changedOptions = CreateOptions(MySqlServerVersion.MariaDb(new Version(11, 8, 0)), MySqlGuidFormat.Binary16);
        var singletonOptions = new MySqlSingletonOptions();

        singletonOptions.Initialize(originalOptions);

        var exception = Assert.Throws<InvalidOperationException>(() => singletonOptions.Validate(changedOptions));

        Assert.Equal("The configured MySQL server version changed for the shared service provider.", exception.Message);
    }

    /// <summary>
    /// Verifies that validation fails when the persisted GUID-format configuration changes.
    /// </summary>
    [Fact]
    public void Validate_rejects_guid_format_changes()
    {
        var originalOptions = CreateOptions(MySqlServerVersion.MySql(new Version(8, 4, 0)), MySqlGuidFormat.Binary16);
        var changedOptions = CreateOptions(MySqlServerVersion.MySql(new Version(8, 4, 0)), MySqlGuidFormat.Char36);
        var singletonOptions = new MySqlSingletonOptions();

        singletonOptions.Initialize(originalOptions);

        var exception = Assert.Throws<InvalidOperationException>(() => singletonOptions.Validate(changedOptions));

        Assert.Equal("The configured MySQL GUID format changed for the shared service provider.", exception.Message);
    }

    private static DbContextOptions CreateOptions(
        MySqlServerVersion serverVersion,
        MySqlGuidFormat defaultGuidFormat
    )
    {
        var builder = new DbContextOptionsBuilder();

        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            serverVersion,
            options => options.DefaultGuidFormat(defaultGuidFormat));

        return builder.Options;
    }
}
