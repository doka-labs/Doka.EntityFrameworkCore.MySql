namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies the live MySqlConnector contracts that the supported driver range
/// must preserve independently of a specific engine family.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
[Trait("Category", "DriverContract")]
public sealed class MySqlDriverCompatibilityTests
{
    /// <summary>
    /// Verifies pooling, pool reset, server-version detection, and provider reuse
    /// against the established MySQL 8.4 release line.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task MySql84_preserves_connection_and_pool_contracts()
    {
        await AssertConnectionAndPoolContractsAsync(
                IntegrationDatabaseTarget.MySql84,
                MySqlServerVersion.MySql(new Version(8, 4, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies pooling, pool reset, server-version detection, and provider reuse
    /// against MySQL 9.7.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public async Task MySql97_preserves_connection_and_pool_contracts()
    {
        await AssertConnectionAndPoolContractsAsync(
                IntegrationDatabaseTarget.MySql97,
                IntegrationTestEnvironment.GetServerVersion(IntegrationDatabaseTarget.MySql97))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies the shared connection and pool contract against MariaDB 10.11.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public async Task MariaDb1011_preserves_connection_and_pool_contracts()
    {
        await AssertConnectionAndPoolContractsAsync(
                IntegrationDatabaseTarget.MariaDb1011,
                IntegrationTestEnvironment.GetServerVersion(IntegrationDatabaseTarget.MariaDb1011))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies the shared connection and pool contract against the oldest
    /// established MariaDB 11.4 release line.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public async Task MariaDb114_preserves_connection_and_pool_contracts()
    {
        await AssertConnectionAndPoolContractsAsync(
                IntegrationDatabaseTarget.MariaDb114,
                MySqlServerVersion.MariaDb(new Version(11, 4, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies pooling, pool reset, server-version detection, and provider reuse
    /// against the established MariaDB 11.8 release line.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_preserves_connection_and_pool_contracts()
    {
        await AssertConnectionAndPoolContractsAsync(
                IntegrationDatabaseTarget.MariaDb118,
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies the shared connection and pool contract against MariaDB 12.3.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public async Task MariaDb123_preserves_connection_and_pool_contracts()
    {
        await AssertConnectionAndPoolContractsAsync(
                IntegrationDatabaseTarget.MariaDb123,
                IntegrationTestEnvironment.GetServerVersion(IntegrationDatabaseTarget.MariaDb123))
            .ConfigureAwait(false);
    }

    private static async Task AssertConnectionAndPoolContractsAsync(
        IntegrationDatabaseTarget target,
        MySqlServerVersion configuredServerVersion
    )
    {
        var connectionString =
            new MySqlConnectionStringBuilder(IntegrationTestEnvironment.GetConnectionString(target))
            {
                Pooling = true,
                MinimumPoolSize = 0,
                MaximumPoolSize = 4,
                ConnectionReset = true,
            }.ConnectionString;

        await MySqlConnection
            .ClearAllPoolsAsync(CancellationToken.None)
            .ConfigureAwait(false);

        int firstServerThread;
        await using (var firstConnection = new MySqlConnection(connectionString))
        {
            await firstConnection
                .OpenAsync()
                .ConfigureAwait(false);
            firstServerThread = firstConnection.ServerThread;

            var detected = MySqlServerVersion.AutoDetect(firstConnection);

            Assert.Equal(configuredServerVersion.IsMariaDb, detected.IsMariaDb);
            Assert.Equal(configuredServerVersion.Version.Major, detected.Version.Major);
            Assert.Equal(configuredServerVersion.Version.Minor, detected.Version.Minor);
        }

        await using (var pooledConnection = new MySqlConnection(connectionString))
        {
            await pooledConnection
                .OpenAsync()
                .ConfigureAwait(false);

            Assert.Equal(firstServerThread, pooledConnection.ServerThread);
        }

        await MySqlConnection
            .ClearAllPoolsAsync(CancellationToken.None)
            .ConfigureAwait(false);

        await using (var replacementConnection = new MySqlConnection(connectionString))
        {
            await replacementConnection
                .OpenAsync()
                .ConfigureAwait(false);

            Assert.NotEqual(firstServerThread, replacementConnection.ServerThread);
        }

        await using var context = new DriverContractContext(
            IntegrationTestDbContextOptions.Create<DriverContractContext>().UseMySql(connectionString, configuredServerVersion)
                .Options);

        Assert.Equal(
            1,
            await context
                .Database.SqlQueryRaw<int>("SELECT 1 AS Value")
                .SingleAsync()
                .ConfigureAwait(false));
    }

    private sealed class DriverContractContext : DbContext
    {
        public DriverContractContext(
            DbContextOptions<DriverContractContext> options
        ) : base(options) { }
    }
}
