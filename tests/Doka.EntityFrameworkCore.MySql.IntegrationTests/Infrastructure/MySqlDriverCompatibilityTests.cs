namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies the live MySqlConnector contracts that the supported driver range
/// must preserve independently of a specific engine family.
/// </summary>
[IntegrationDatabaseTest]
// WHY: ClearAllPoolsAsync affects every connection string in this process;
// concurrent pool-reuse assertions must not observe this class's reset.
[Collection(ProcessWideIntegrationTestGroup.Name)]
[Trait("Category", "DriverContract")]
public sealed class MySqlDriverCompatibilityTests
{
    /// <summary>
    /// Reports each ambient-transaction probe separately for every selected engine.
    /// </summary>
    public static TheoryData<string, bool?, bool> AutoDetectCases => CreateAutoDetectCases();

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
        // Arrange
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

        // Act
        var exception = Record.Exception(() => MySqlServerVersion.AutoDetect(connectionString));

        // Assert
        Assert.IsType<MySqlException>(exception);
    }

    /// <summary>Verifies that server-version detection cannot enlist in an ambient transaction.</summary>
    [Theory]
    [MemberData(nameof(AutoDetectCases))]
    public Task AutoDetect_connection_string_does_not_join_the_ambient_transaction(
        string targetName,
        bool? autoEnlist,
        bool useXaTransactions
    ) => AssertAutoDetectDoesNotJoinAmbientTransactionAsync(
        Enum.Parse<IntegrationDatabaseTarget>(targetName),
        autoEnlist,
        useXaTransactions);

    private static TheoryData<string, bool?, bool> CreateAutoDetectCases()
    {
        var cases = new TheoryData<string, bool?, bool>();

        // WHY: Public xUnit theory methods cannot expose the internal target enum.
        foreach (var target in IntegrationTestEnvironment.GetSelectedTargets())
        {
            cases.Add(target.ToString(), null, false);
            cases.Add(target.ToString(), true, false);
            cases.Add(target.ToString(), false, false);
            cases.Add(target.ToString(), null, true);
            cases.Add(target.ToString(), true, true);
            cases.Add(target.ToString(), false, true);
        }

        return cases;
    }

    private static async Task AssertAutoDetectDoesNotJoinAmbientTransactionAsync(
        IntegrationDatabaseTarget target,
        bool? autoEnlist,
        bool useXaTransactions
    )
    {
        // Arrange
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

        // WHY: A live non-XA ambient transaction is the fixture precondition
        // that makes the following isolated version probe meaningful.
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

        // Act
        // WHY: The synchronous overload is the public contract under test.
        // ReSharper disable once MethodHasAsyncOverload
        var detected = MySqlServerVersion.AutoDetect(builder.ConnectionString);

        // WHY: Caller-owned synchronous detection must retain the ambient connection.
        // ReSharper disable once MethodHasAsyncOverload
        var callerDetected = MySqlServerVersion.AutoDetect(callerConnection);

        // WHY: This is the explicit synchronous compatibility-mode overload.
        // ReSharper disable once MethodHasAsyncOverload
        var callerOwned = MySqlServerVersion.AutoDetect(
            callerConnection,
            MySqlServerVersionCompatibilityMode.AllowUnsupported);

        var currentTransaction = System.Transactions.Transaction.Current;
        var transactionStatus = transaction.TransactionInformation.Status;
        var connectionState = callerConnection.State;
        var currentServerThread = callerConnection.ServerThread;
        await using var stillRejectedConnection = new MySqlConnection(callerConnectionString);
        var stillRejected = await Record
            .ExceptionAsync(() => stillRejectedConnection.OpenAsync(CancellationToken.None))
            .ConfigureAwait(true);

        // Assert
        Assert.Equal(expected.Version, detected.Version);
        Assert.Equal(expected.IsMariaDb, detected.IsMariaDb);
        Assert.Equal(MySqlServerVersionCompatibilityMode.SupportedOnly, detected.CompatibilityMode);
        Assert.Same(transaction, currentTransaction);
        Assert.Equal(System.Transactions.TransactionStatus.Active, transactionStatus);
        Assert.Equal(expected.Version, callerDetected.Version);
        Assert.Equal(expected.Version, callerOwned.Version);
        Assert.Equal(MySqlServerVersionCompatibilityMode.AllowUnsupported, callerOwned.CompatibilityMode);
        Assert.Equal(ConnectionState.Open, connectionState);
        Assert.Equal(serverThread, currentServerThread);
        Assert.IsType<NotSupportedException>(stillRejected);

        scope.Complete();
    }

    private static async Task AssertConnectionAndPoolContractsAsync(
        IntegrationDatabaseTarget target,
        MySqlServerVersion configuredServerVersion
    )
    {
        // Arrange
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

        // Act
        // WHY: Repeated synchronous probes qualify pool return on this API path.
        // ReSharper disable once MethodHasAsyncOverload
        var detectedFromConnectionString = MySqlServerVersion.AutoDetect(connectionString);
        var repeatedDetections = new List<MySqlServerVersion>();

        for (var iteration = 0; iteration < 3; iteration++)
        {
            // WHY: Each synchronous probe must return its pool lease before the next.
            // ReSharper disable once MethodHasAsyncOverload
            repeatedDetections.Add(MySqlServerVersion.AutoDetect(connectionString));
        }

        int firstServerThread;
        MySqlServerVersion detectedFromConnection;

        await using (var firstConnection = new MySqlConnection(connectionString))
        {
            await firstConnection.OpenAsync().ConfigureAwait(false);
            firstServerThread = firstConnection.ServerThread;

            // WHY: This overload reads the caller-owned synchronous connection.
            // ReSharper disable once MethodHasAsyncOverload
            detectedFromConnection = MySqlServerVersion.AutoDetect(firstConnection);
        }

        int reusedServerThread;

        await using (var pooledConnection = new MySqlConnection(connectionString))
        {
            await pooledConnection.OpenAsync().ConfigureAwait(false);

            reusedServerThread = pooledConnection.ServerThread;
        }

        await MySqlConnection
            .ClearAllPoolsAsync(CancellationToken.None)
            .ConfigureAwait(false);

        int replacementServerThread;

        await using (var replacementConnection = new MySqlConnection(connectionString))
        {
            await replacementConnection
                .OpenAsync()
                .ConfigureAwait(false);

            replacementServerThread = replacementConnection.ServerThread;
        }

        await using var context = new DriverContractContext(
            IntegrationTestDbContextOptions.Create<DriverContractContext>().UseMySql(connectionString, configuredServerVersion)
                .Options);

        var scalar = await context
            .Database.SqlQueryRaw<int>("SELECT 1 AS Value")
            .SingleAsync()
            .ConfigureAwait(false);

        var failingConnectionString = new MySqlConnectionStringBuilder(connectionString)
        {
            Database = $"doka_autodetect_missing_{Guid.NewGuid():N}",
        }.ConnectionString;

        var failures = new List<Exception?>();

        for (var iteration = 0; iteration < 3; iteration++)
        {
            // WHY: Failed synchronous probes must return their pool leases too.
            // ReSharper disable once MethodHasAsyncOverload
            failures.Add(Record.Exception(() => MySqlServerVersion.AutoDetect(failingConnectionString)));
        }

        // WHY: Recovery must use the same synchronous path that just failed.
        // ReSharper disable once MethodHasAsyncOverload
        var recoveredDetection = MySqlServerVersion.AutoDetect(connectionString);

        // Assert
        Assert.Equal(configuredServerVersion.IsMariaDb, detectedFromConnectionString.IsMariaDb);
        Assert.Equal(configuredServerVersion.Version.Major, detectedFromConnectionString.Version.Major);
        Assert.Equal(configuredServerVersion.Version.Minor, detectedFromConnectionString.Version.Minor);
        Assert.Equal(MySqlServerVersionCompatibilityMode.SupportedOnly, detectedFromConnectionString.CompatibilityMode);
        Assert.All(repeatedDetections, detection =>
        {
            Assert.Equal(detectedFromConnectionString.Version, detection.Version);
            Assert.Equal(detectedFromConnectionString.IsMariaDb, detection.IsMariaDb);
        });
        Assert.Equal(configuredServerVersion.IsMariaDb, detectedFromConnection.IsMariaDb);
        Assert.Equal(configuredServerVersion.Version.Major, detectedFromConnection.Version.Major);
        Assert.Equal(configuredServerVersion.Version.Minor, detectedFromConnection.Version.Minor);
        Assert.Equal(detectedFromConnectionString.Version, detectedFromConnection.Version);
        Assert.Equal(detectedFromConnectionString.SupportStatus, detectedFromConnection.SupportStatus);
        Assert.Equal(detectedFromConnectionString.CompatibilityMode, detectedFromConnection.CompatibilityMode);
        Assert.Equal(firstServerThread, reusedServerThread);
        Assert.NotEqual(firstServerThread, replacementServerThread);
        Assert.Equal(1, scalar);
        Assert.All(failures, failure =>
            Assert.Equal(MySqlErrorCode.UnknownDatabase, Assert.IsType<MySqlException>(failure).ErrorCode));
        Assert.Equal(detectedFromConnectionString.Version, recoveredDetection.Version);
    }

    private sealed class DriverContractContext : DbContext
    {
        public DriverContractContext(
            DbContextOptions<DriverContractContext> options
        ) : base(options) { }
    }
}
