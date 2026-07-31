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
    /// against the supported MySQL release line.
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
    /// against the supported MariaDB release line.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_preserves_connection_and_pool_contracts()
    {
        await AssertConnectionAndPoolContractsAsync(
                IntegrationDatabaseTarget.MariaDb118,
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)))
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
            new DbContextOptionsBuilder<DriverContractContext>().UseMySql(connectionString, configuredServerVersion)
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
