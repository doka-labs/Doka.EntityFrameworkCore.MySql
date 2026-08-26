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

    /// <summary>
    /// Verifies that connection-string detection exposes an unreachable server
    /// as a driver connection failure.
    /// </summary>
    [Fact]
    public void AutoDetect_connection_string_propagates_an_unreachable_server()
    {
        using var reservedPort = new System.Net.Sockets.Socket(
            System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Stream,
            System.Net.Sockets.ProtocolType.Tcp);

        reservedPort.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
        var endpoint = Assert.IsType<System.Net.IPEndPoint>(reservedPort.LocalEndPoint);
        var connectionString = new MySqlConnectionStringBuilder
        {
            Server = "127.0.0.1",
            Port = (uint)endpoint.Port,
            UserID = "doka-unreachable-test",
            ConnectionTimeout = 1,
            Pooling = false,
            SslMode = MySqlSslMode.None,
        }.ConnectionString;

        Assert.Throws<MySqlException>(() => MySqlServerVersion.AutoDetect(connectionString));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(null, true)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task AutoDetect_connection_string_does_not_join_the_ambient_transaction(
        bool? autoEnlist,
        bool useXaTransactions
    )
    {
        foreach (var target in IntegrationTestEnvironment.GetSelectedTargets())
        {
            var builder = new MySqlConnectionStringBuilder(IntegrationTestEnvironment.GetConnectionString(target))
            {
                AutoEnlist = true,
                UseXaTransactions = false,
                Pooling = false,
            };

            var callerConnectionString = builder.ConnectionString;
            using var scope = new System.Transactions.TransactionScope(
                System.Transactions.TransactionScopeOption.RequiresNew,
                System.Transactions.TransactionScopeAsyncFlowOption.Enabled);

            await using var callerConnection = new MySqlConnection(callerConnectionString);
            await callerConnection
                .OpenAsync(CancellationToken.None)
                .ConfigureAwait(true);

            var transaction = System.Transactions.Transaction.Current;
            Assert.NotNull(transaction);

            var serverThread = callerConnection.ServerThread;
            var expected = MySqlServerVersion.Parse(callerConnection.ServerVersion);

            // A second participant is forbidden in a non-XA transaction. This
            // control makes a successful version probe evidence of isolation.
            await using var rejectedConnection = new MySqlConnection(callerConnectionString);
            await Assert
                .ThrowsAsync<NotSupportedException>(() => rejectedConnection.OpenAsync(CancellationToken.None))
                .ConfigureAwait(true);

            builder.UseXaTransactions = useXaTransactions;
            if (autoEnlist.HasValue)
            {
                builder.AutoEnlist = autoEnlist.Value;
            }
            else
            {
                builder.Remove("Auto Enlist");
            }

            var detected = MySqlServerVersion.AutoDetect(builder.ConnectionString);

            Assert.Equal(expected.Version, detected.Version);
            Assert.Equal(expected.IsMariaDb, detected.IsMariaDb);
            Assert.Equal(MySqlServerVersionCompatibilityMode.SupportedOnly, detected.CompatibilityMode);
            Assert.Same(transaction, System.Transactions.Transaction.Current);
            Assert.Equal(System.Transactions.TransactionStatus.Active, transaction.TransactionInformation.Status);

            Assert.Equal(expected.Version, MySqlServerVersion.AutoDetect(callerConnection).Version);
            var callerOwned = MySqlServerVersion.AutoDetect(
                callerConnection,
                MySqlServerVersionCompatibilityMode.AllowUnsupported);

            Assert.Equal(expected.Version, callerOwned.Version);
            Assert.Equal(MySqlServerVersionCompatibilityMode.AllowUnsupported, callerOwned.CompatibilityMode);
            Assert.Equal(ConnectionState.Open, callerConnection.State);
            Assert.Equal(serverThread, callerConnection.ServerThread);

            await using var stillRejectedConnection = new MySqlConnection(callerConnectionString);
            await Assert
                .ThrowsAsync<NotSupportedException>(() => stillRejectedConnection.OpenAsync(CancellationToken.None))
                .ConfigureAwait(true);

            scope.Complete();
        }
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
                MaximumPoolSize = 1,
                ConnectionTimeout = 5,
                ConnectionReset = true,
            }.ConnectionString;

        await MySqlConnection
            .ClearAllPoolsAsync(CancellationToken.None)
            .ConfigureAwait(false);

        var detectedFromConnectionString = MySqlServerVersion.AutoDetect(connectionString);

        Assert.Equal(configuredServerVersion.IsMariaDb, detectedFromConnectionString.IsMariaDb);
        Assert.Equal(configuredServerVersion.Version.Major, detectedFromConnectionString.Version.Major);
        Assert.Equal(configuredServerVersion.Version.Minor, detectedFromConnectionString.Version.Minor);
        Assert.Equal(MySqlServerVersionCompatibilityMode.SupportedOnly, detectedFromConnectionString.CompatibilityMode);

        for (var iteration = 0; iteration < 3; iteration++)
        {
            var repeatedDetection = MySqlServerVersion.AutoDetect(connectionString);

            Assert.Equal(detectedFromConnectionString.Version, repeatedDetection.Version);
            Assert.Equal(detectedFromConnectionString.IsMariaDb, repeatedDetection.IsMariaDb);
        }

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
            Assert.Equal(detectedFromConnectionString.Version, detected.Version);
            Assert.Equal(detectedFromConnectionString.SupportStatus, detected.SupportStatus);
            Assert.Equal(detectedFromConnectionString.CompatibilityMode, detected.CompatibilityMode);
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

        var failingConnectionString = new MySqlConnectionStringBuilder(connectionString)
        {
            Database = $"doka_autodetect_missing_{Guid.NewGuid():N}",
        }.ConnectionString;

        for (var iteration = 0; iteration < 3; iteration++)
        {
            var failure = Assert.Throws<MySqlException>(() => MySqlServerVersion.AutoDetect(failingConnectionString));

            Assert.Equal(MySqlErrorCode.UnknownDatabase, failure.ErrorCode);
        }

        Assert.Equal(detectedFromConnectionString.Version, MySqlServerVersion.AutoDetect(connectionString).Version);
    }

    private sealed class DriverContractContext : DbContext
    {
        public DriverContractContext(
            DbContextOptions<DriverContractContext> options
        ) : base(options) { }
    }
}
