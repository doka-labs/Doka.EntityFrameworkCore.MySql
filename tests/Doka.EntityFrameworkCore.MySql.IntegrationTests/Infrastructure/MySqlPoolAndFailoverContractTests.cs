using System.Net;
using System.Net.Sockets;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies bounded pool behavior, session cleanup, broken-connection eviction,
/// and ordered multi-host failover through the provider's connection path.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
[Trait("Category", "FailureConfigurationContract")]
[Trait("VerificationLane", "FullIntegration")]
public sealed class MySqlPoolAndFailoverContractTests
{
    /// <summary>
    /// Verifies the pool and failover contract against MySQL 8.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task MySql84_satisfies_the_pool_and_failover_contract()
    {
        await AssertPoolAndFailoverContractAsync(
                IntegrationDatabaseTarget.MySql84,
                MySqlServerVersion.MySql(new Version(8, 4, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies the pool and failover contract against MySQL 9.7.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public async Task MySql97_satisfies_the_pool_and_failover_contract()
    {
        await AssertPoolAndFailoverContractAsync(
                IntegrationDatabaseTarget.MySql97,
                IntegrationTestEnvironment.GetServerVersion(IntegrationDatabaseTarget.MySql97))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies the pool and failover contract against MariaDB 10.11.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public async Task MariaDb1011_satisfies_the_pool_and_failover_contract()
    {
        await AssertPoolAndFailoverContractAsync(
                IntegrationDatabaseTarget.MariaDb1011,
                IntegrationTestEnvironment.GetServerVersion(IntegrationDatabaseTarget.MariaDb1011))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies the pool and failover contract against MariaDB 11.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public async Task MariaDb114_satisfies_the_pool_and_failover_contract()
    {
        await AssertPoolAndFailoverContractAsync(
                IntegrationDatabaseTarget.MariaDb114,
                MySqlServerVersion.MariaDb(new Version(11, 4, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies the pool and failover contract against MariaDB 11.8.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_satisfies_the_pool_and_failover_contract()
    {
        await AssertPoolAndFailoverContractAsync(
                IntegrationDatabaseTarget.MariaDb118,
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies the pool and failover contract against MariaDB 12.3.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public async Task MariaDb123_satisfies_the_pool_and_failover_contract()
    {
        await AssertPoolAndFailoverContractAsync(
                IntegrationDatabaseTarget.MariaDb123,
                IntegrationTestEnvironment.GetServerVersion(IntegrationDatabaseTarget.MariaDb123))
            .ConfigureAwait(false);
    }

    private static async Task AssertPoolAndFailoverContractAsync(
        IntegrationDatabaseTarget target,
        MySqlServerVersion serverVersion
    )
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);

        await AssertPoolSaturationAndRecoveryAsync(connectionString, serverVersion)
            .ConfigureAwait(false);
        await AssertPoolWaitCanBeCancelledAsync(connectionString)
            .ConfigureAwait(false);
        await AssertSessionResetAsync(connectionString)
            .ConfigureAwait(false);
        await AssertBrokenConnectionIsEvictedAsync(connectionString)
            .ConfigureAwait(false);
        await AssertMultiHostFailoverAsync(connectionString, serverVersion)
            .ConfigureAwait(false);
    }

    private static async Task AssertPoolSaturationAndRecoveryAsync(
        string baseConnectionString,
        MySqlServerVersion serverVersion
    )
    {
        var connectionString = CreateIsolatedPoolConnectionString(
            baseConnectionString,
            maximumPoolSize: 2,
            connectionTimeout: 1);
        await using var firstConnection = new MySqlConnection(connectionString);
        await MySqlConnection
            .ClearPoolAsync(firstConnection, CancellationToken.None)
            .ConfigureAwait(false);

        try
        {
            await using var secondConnection = new MySqlConnection(connectionString);
            await firstConnection
                .OpenAsync()
                .ConfigureAwait(false);
            await secondConnection
                .OpenAsync()
                .ConfigureAwait(false);

            await using (var timedOutConnection = new MySqlConnection(connectionString))
            {
                _ = await Assert
                    .ThrowsAsync<MySqlException>(() => timedOutConnection.OpenAsync())
                    .ConfigureAwait(false);
            }

            var releasedServerThread = firstConnection.ServerThread;
            await firstConnection
                .CloseAsync()
                .ConfigureAwait(false);

            await using var recoveredConnection = new MySqlConnection(connectionString);
            await recoveredConnection
                .OpenAsync()
                .ConfigureAwait(false);

            Assert.Equal(releasedServerThread, recoveredConnection.ServerThread);

            await using var context = new PoolContractContext(
                IntegrationTestDbContextOptions.Create<PoolContractContext>().UseMySql(recoveredConnection, serverVersion)
                    .Options);

            Assert.Equal(
                1,
                await context
                    .Database.SqlQueryRaw<int>("SELECT 1 AS Value")
                    .SingleAsync()
                    .ConfigureAwait(false));
        }
        finally
        {
            await MySqlConnection
                .ClearPoolAsync(firstConnection, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private static async Task AssertPoolWaitCanBeCancelledAsync(
        string baseConnectionString
    )
    {
        // Keep the connection timeout well outside the cancellation path. A
        // timer race between both outcomes made scheduler load decide which
        // exception the contract observed.
        var connectionString = CreateIsolatedPoolConnectionString(
            baseConnectionString,
            maximumPoolSize: 1,
            connectionTimeout: 30);
        await using var blockingConnection = new MySqlConnection(connectionString);
        await MySqlConnection
            .ClearPoolAsync(blockingConnection, CancellationToken.None)
            .ConfigureAwait(false);

        try
        {
            await blockingConnection
                .OpenAsync()
                .ConfigureAwait(false);

            await using var cancelledConnection = new MySqlConnection(connectionString);
            using var cancellation = new CancellationTokenSource();
            var pendingOpen = cancelledConnection.OpenAsync(cancellation.Token);

            Assert.False(pendingOpen.IsCompleted);
            await cancellation
                .CancelAsync()
                .ConfigureAwait(false);

            _ = await Assert
                .ThrowsAnyAsync<OperationCanceledException>(() => pendingOpen)
                .ConfigureAwait(false);
        }
        finally
        {
            await MySqlConnection
                .ClearPoolAsync(blockingConnection, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private static async Task AssertSessionResetAsync(
        string baseConnectionString
    )
    {
        var connectionString = CreateIsolatedPoolConnectionString(
            baseConnectionString,
            maximumPoolSize: 1,
            connectionTimeout: 2);
        await using var initialConnection = new MySqlConnection(connectionString);
        await MySqlConnection
            .ClearPoolAsync(initialConnection, CancellationToken.None)
            .ConfigureAwait(false);

        try
        {
            await initialConnection
                .OpenAsync()
                .ConfigureAwait(false);
            var initialServerThread = initialConnection.ServerThread;
            var initialSqlMode = await ReadSessionSqlModeAsync(initialConnection)
                .ConfigureAwait(false);

            await using (var command = initialConnection.CreateCommand())
            {
                // User variables, SQL mode, and temporary tables exercise three
                // independent categories of state that must not leak between
                // logical users of the same physical pooled connection.
                command.CommandText = "SET @doka_session_marker = 41;"
                    + "SET SESSION sql_mode = 'ANSI_QUOTES,NO_BACKSLASH_ESCAPES';"
                    + "CREATE TEMPORARY TABLE `DokaPoolResetMarker` (`Id` int NOT NULL);";
                _ = await command
                    .ExecuteNonQueryAsync()
                    .ConfigureAwait(false);
            }

            await initialConnection
                .CloseAsync()
                .ConfigureAwait(false);

            await using var reusedConnection = new MySqlConnection(connectionString);
            await reusedConnection
                .OpenAsync()
                .ConfigureAwait(false);

            Assert.Equal(initialServerThread, reusedConnection.ServerThread);

            await using var markerCommand = reusedConnection.CreateCommand();
            markerCommand.CommandText = "SELECT @doka_session_marker;";

            Assert.Equal(
                DBNull.Value,
                await markerCommand
                    .ExecuteScalarAsync()
                    .ConfigureAwait(false));

            Assert.Equal(
                initialSqlMode,
                await ReadSessionSqlModeAsync(reusedConnection)
                    .ConfigureAwait(false));

            _ = await Assert
                .ThrowsAsync<MySqlException>(() => ExecuteScalarAsync(
                    reusedConnection,
                    "SELECT COUNT(*) FROM `DokaPoolResetMarker`;"))
                .ConfigureAwait(false);
        }
        finally
        {
            await MySqlConnection
                .ClearPoolAsync(initialConnection, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private static async Task AssertBrokenConnectionIsEvictedAsync(
        string directConnectionString
    )
    {
        var directBuilder = new MySqlConnectionStringBuilder(directConnectionString);
        await using var proxy = new TcpFaultProxy(directBuilder.Server, checked((int)directBuilder.Port));
        var proxiedBuilder = new MySqlConnectionStringBuilder(proxy.BuildConnectionString(directConnectionString))
        {
            Pooling = true,
            ConnectionReset = true,
            MaximumPoolSize = 1,
            ConnectionTimeout = 2,
            ApplicationName = $"doka-broken-pool-{Guid.NewGuid():N}",
        };
        await using var faultedConnection = new MySqlConnection(proxiedBuilder.ConnectionString);
        await MySqlConnection
            .ClearPoolAsync(faultedConnection, CancellationToken.None)
            .ConfigureAwait(false);

        try
        {
            await faultedConnection
                .OpenAsync()
                .ConfigureAwait(false);
            var faultedServerThread = faultedConnection.ServerThread;

            Assert.True(proxy.DropActiveConnections() > 0);
            var fault = await Record
                .ExceptionAsync(() => ExecuteScalarAsync(faultedConnection, "SELECT 1"))
                .ConfigureAwait(false);

            Assert.True(
                fault is MySqlException or SocketException,
                $"Expected a provider or socket failure, but observed {fault?.GetType().FullName ?? "no failure"}.");

            await faultedConnection
                .CloseAsync()
                .ConfigureAwait(false);

            await using var replacementConnection = new MySqlConnection(proxiedBuilder.ConnectionString);
            await replacementConnection
                .OpenAsync()
                .ConfigureAwait(false);

            Assert.NotEqual(faultedServerThread, replacementConnection.ServerThread);
            Assert.Equal(
                1L,
                Convert.ToInt64(
                    await ExecuteScalarAsync(replacementConnection, "SELECT 1")
                        .ConfigureAwait(false),
                    CultureInfo.InvariantCulture));
        }
        finally
        {
            await MySqlConnection
                .ClearPoolAsync(faultedConnection, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private static async Task AssertMultiHostFailoverAsync(
        string directConnectionString,
        MySqlServerVersion serverVersion
    )
    {
        var directBuilder = new MySqlConnectionStringBuilder(directConnectionString);
        var healthyHost = IPAddress.IPv6Loopback;
        await using var proxy = new TcpFaultProxy(
            directBuilder.Server,
            checked((int)directBuilder.Port),
            healthyHost);
        var unavailableHost = IPAddress.Loopback;

        using (var unavailableClient = new TcpClient(AddressFamily.InterNetwork))
        {
            // Use IP literals on the two platform-defined loopback endpoints.
            // The first refuses immediately while the proxy listens on the
            // second, so the test measures ordered host failover rather than
            // DNS or an operating-system connect timeout.
            _ = await Assert
                .ThrowsAsync<SocketException>(() => unavailableClient.ConnectAsync(unavailableHost, proxy.Port))
                .ConfigureAwait(false);
        }

        var failoverConnectionString = new MySqlConnectionStringBuilder(directConnectionString)
        {
            Server = $"{unavailableHost},{proxy.Host}",
            Port = (uint)proxy.Port,
            LoadBalance = MySqlLoadBalance.FailOver,
            Pooling = false,
            ConnectionTimeout = 2,
            SslMode = MySqlSslMode.Disabled,
        }.ConnectionString;

        await using var context = new PoolContractContext(
            IntegrationTestDbContextOptions.Create<PoolContractContext>().UseMySql(failoverConnectionString, serverVersion)
                .Options);

        Assert.Equal(
            1,
            await context
                .Database.SqlQueryRaw<int>("SELECT 1 AS Value")
                .SingleAsync()
                .ConfigureAwait(false));
        Assert.Contains(proxy.GetObservedQueries(), query => query.Contains("SELECT 1", StringComparison.Ordinal));
    }

    private static string CreateIsolatedPoolConnectionString(
        string baseConnectionString,
        uint maximumPoolSize,
        uint connectionTimeout
    )
    {
        return new MySqlConnectionStringBuilder(baseConnectionString)
        {
            Pooling = true,
            ConnectionReset = true,
            AllowUserVariables = true,
            MinimumPoolSize = 0,
            MaximumPoolSize = maximumPoolSize,
            ConnectionTimeout = connectionTimeout,
            ApplicationName = $"doka-pool-contract-{Guid.NewGuid():N}",
        }.ConnectionString;
    }

    private static async Task<object?> ExecuteScalarAsync(
        MySqlConnection connection,
        string commandText
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;

        return await command
            .ExecuteScalarAsync()
            .ConfigureAwait(false);
    }

    private static async Task<string> ReadSessionSqlModeAsync(
        MySqlConnection connection
    )
    {
        return Assert.IsType<string>(
            await ExecuteScalarAsync(connection, "SELECT @@SESSION.sql_mode;")
                .ConfigureAwait(false));
    }

    private sealed class PoolContractContext : DbContext
    {
        public PoolContractContext(
            DbContextOptions<PoolContractContext> options
        ) : base(options) { }
    }
}
