namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies that every Guid live-test bootstrap uses the repository's
/// intentionally transient internal-provider policy.
/// </summary>
public sealed class MySqlGuidFormatTestOptionsTests
{
    private const string ConnectionString =
        "Server=127.0.0.1;Port=3306;Database=doka_guid_options;User ID=root;Password=root_password;"
        + "GuidFormat=Binary16;";

    private static readonly MySqlServerVersion s_serverVersion = MySqlServerVersion.MariaDb(new Version(11, 8, 0));

    /// <summary>
    /// Prevents a complete Rider run from making EF Core's process-wide service
    /// provider threshold dependent on the order of the Guid live tests.
    /// </summary>
    [Theory]
    [InlineData("binary16")]
    [InlineData("connection-string")]
    [InlineData("db-connection")]
    [InlineData("data-source")]
    public void Guid_live_test_options_are_transient_for_every_bootstrap(
        string bootstrap
    )
    {
        // Arrange
        using var connection = bootstrap == "db-connection" ? new MySqlConnection(ConnectionString) : null;
        using var dataSource = bootstrap == "data-source" ? new MySqlDataSourceBuilder(ConnectionString).Build() : null;

        // Act
        var options = bootstrap switch
        {
            "binary16" => MySqlGuidFormatTestOptions.BuildOptions<DbContext>(
                ConnectionString,
                "doka_guid_options_binary16",
                s_serverVersion),
            "connection-string" => MySqlGuidFormatTestOptions.BuildDefaultChar36Options<DbContext>(
                ConnectionString,
                s_serverVersion),
            "db-connection" => MySqlGuidFormatTestOptions.BuildDefaultChar36Options<DbContext>(
                connection ?? throw new InvalidOperationException("The connection fixture is missing."),
                s_serverVersion),
            "data-source" => MySqlGuidFormatTestOptions.BuildDefaultChar36Options<DbContext>(
                dataSource ?? throw new InvalidOperationException("The data-source fixture is missing."),
                s_serverVersion),
            _ => throw new ArgumentOutOfRangeException(nameof(bootstrap), bootstrap, "Unknown Guid bootstrap path."),
        };

        // Assert
        var coreOptions = options.FindExtension<CoreOptionsExtension>();

        Assert.NotNull(coreOptions);
        Assert.False(coreOptions.ServiceProviderCachingEnabled);
        Assert.Equal(
            WarningBehavior.Log,
            coreOptions.WarningsConfiguration.GetBehavior(CoreEventId.ManyServiceProvidersCreatedWarning));
    }
}
