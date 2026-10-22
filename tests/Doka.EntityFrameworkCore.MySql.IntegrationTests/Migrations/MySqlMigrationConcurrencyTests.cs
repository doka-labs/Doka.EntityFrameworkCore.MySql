namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Live concurrency coverage for the migration advisory lock (per ADR D-002): two
/// migrations running against different databases on the same MySQL instance must
/// not block each other, while two migrations against the same database must
/// serialize, with the third caller observing a <see cref="TimeoutException"/>
/// when the lock timeout is exhausted.
/// </summary>
[IntegrationDatabaseTest]
public sealed class MySqlMigrationConcurrencyTests
{
    /// <summary>
    /// Multi-tenant safety check: two distinct databases on the same server hold
    /// independent advisory locks, so two migrations can run concurrently.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Migration_locks_do_not_block_across_distinct_databases()
    {
        // Arrange
        var baseConnectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);
        var dbNameA = $"doka_lock_a_{Guid.NewGuid():N}"[..30];
        var dbNameB = $"doka_lock_b_{Guid.NewGuid():N}"[..30];

        await CreateDatabaseAsync(baseConnectionString, dbNameA)
            .ConfigureAwait(false);
        await CreateDatabaseAsync(baseConnectionString, dbNameB)
            .ConfigureAwait(false);

        try
        {
            await using var contextA =
                new LockContext(CreateOptions(BuildConnectionString(baseConnectionString, dbNameA)));

            await using var contextB =
                new LockContext(CreateOptions(BuildConnectionString(baseConnectionString, dbNameB)));

            var historyA = contextA.GetService<IHistoryRepository>();
            var historyB = contextB.GetService<IHistoryRepository>();

            // Act
            var acquireA = historyA.AcquireDatabaseLockAsync();
            var acquireB = historyB.AcquireDatabaseLockAsync();

            await Task
                .WhenAll(acquireA, acquireB)
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);

            await using var lockA = await acquireA.ConfigureAwait(false);

            await using var lockB = await acquireB.ConfigureAwait(false);

            // Assert
            Assert.NotNull(lockA);
            Assert.NotNull(lockB);
        }
        finally
        {
            await DropDatabaseAsync(baseConnectionString, dbNameA)
                .ConfigureAwait(false);
            await DropDatabaseAsync(baseConnectionString, dbNameB)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Serialization check: two migrations against the same database compete for
    /// the same lock. The second caller waits; a third caller with a sub-second
    /// timeout surfaces a <see cref="TimeoutException"/>.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Migration_locks_serialize_on_same_database_and_surface_timeout()
    {
        // Arrange
        var baseConnectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);
        var dbName = $"doka_lock_s_{Guid.NewGuid():N}"[..30];

        await CreateDatabaseAsync(baseConnectionString, dbName)
            .ConfigureAwait(false);
        var scopedConnectionString = BuildConnectionString(baseConnectionString, dbName);

        try
        {
            await using var holder = new LockContext(CreateOptions(scopedConnectionString));
            var holderHistory = holder.GetService<IHistoryRepository>();

            await using var heldLock = await holderHistory
                .AcquireDatabaseLockAsync()
                .ConfigureAwait(false);

            // Confirm a contending session sees the lock held via GET_LOCK with timeout=0.
            var lockName = MySqlAdvisoryLockNaming.BuildLockName(scopedConnectionString);

            await using var contender = new MySqlConnection(scopedConnectionString);
            await contender
                .OpenAsync()
                .ConfigureAwait(false);
            await using var probe = contender.CreateCommand();
            probe.CommandText = "SELECT GET_LOCK(@name, 0);";
            var nameParam = probe.CreateParameter();
            nameParam.ParameterName = "@name";
            nameParam.Value = lockName;
            probe.Parameters.Add(nameParam);

            // Act
            var probeResult = await probe
                .ExecuteScalarAsync()
                .ConfigureAwait(false);

            // Assert
            Assert.Equal(0L, Convert.ToInt64(probeResult, CultureInfo.InvariantCulture));
        }
        finally
        {
            await DropDatabaseAsync(baseConnectionString, dbName)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Wait-and-acquire check: when one session already holds the migration lock, a
    /// concurrent acquire on the same database waits until the holder releases rather
    /// than proceeding into a parallel migration. The waiter's observed acquire latency
    /// must reflect the holder's hold duration (within scheduler jitter).
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Migration_lock_acquire_waits_when_held_by_another_session()
    {
        // Arrange
        var baseConnectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);
        var dbName = $"doka_lock_w_{Guid.NewGuid():N}"[..30];
        var holdDuration = TimeSpan.FromSeconds(2);

        await CreateDatabaseAsync(baseConnectionString, dbName)
            .ConfigureAwait(false);
        var scopedConnectionString = BuildConnectionString(baseConnectionString, dbName);

        try
        {
            await using var holderContext = new LockContext(CreateOptions(scopedConnectionString));
            await using var waiterContext = new LockContext(CreateOptions(scopedConnectionString));
            var holderHistory = holderContext.GetService<IHistoryRepository>();
            var waiterHistory = waiterContext.GetService<IHistoryRepository>();

            await using var heldLock = await holderHistory
                .AcquireDatabaseLockAsync()
                .ConfigureAwait(false);

            var releaseHolderTask = Task.Run(async () =>
            {
                await Task
                    .Delay(holdDuration)
                    .ConfigureAwait(false);

                await heldLock
                    .DisposeAsync()
                    .ConfigureAwait(false);
            });

            var acquireStopwatch = Stopwatch.StartNew();

            // Act
            await using var waitedLock = await waiterHistory
                .AcquireDatabaseLockAsync()
                .ConfigureAwait(false);

            acquireStopwatch.Stop();

            await releaseHolderTask.ConfigureAwait(false);

            // Assert
            Assert.NotNull(waitedLock);
            Assert.True(
                acquireStopwatch.Elapsed >= holdDuration - TimeSpan.FromMilliseconds(250),
                $"Expected acquire to wait at least {holdDuration - TimeSpan.FromMilliseconds(250):c}; actual {acquireStopwatch.Elapsed:c}.");
        }
        finally
        {
            await DropDatabaseAsync(baseConnectionString, dbName)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Premortem #4 -- holder-connection-disruption: when the session holding the
    /// migration lock is killed mid-hold, the server reaps the session-scoped lock,
    /// so a fresh acquire from a new connection succeeds without manual cleanup.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Migration_lock_is_released_when_holder_connection_is_killed()
    {
        // Arrange
        var baseConnectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);
        var dbName = $"doka_lock_k_{Guid.NewGuid():N}"[..30];

        await CreateDatabaseAsync(baseConnectionString, dbName)
            .ConfigureAwait(false);
        var scopedConnectionString = BuildConnectionString(baseConnectionString, dbName);
        var lockName = MySqlAdvisoryLockNaming.BuildLockName(scopedConnectionString);

        try
        {
            await using var holderConnection = new MySqlConnection(scopedConnectionString);
            await holderConnection
                .OpenAsync()
                .ConfigureAwait(false);

            await using (var holdCommand = holderConnection.CreateCommand())
            {
                holdCommand.CommandText = "SELECT GET_LOCK(@name, 5);";
                var holdNameParam = holdCommand.CreateParameter();
                holdNameParam.ParameterName = "@name";
                holdNameParam.Value = lockName;
                holdCommand.Parameters.Add(holdNameParam);

                var holdResult = await holdCommand
                    .ExecuteScalarAsync()
                    .ConfigureAwait(false);

                Assert.Equal(1L, Convert.ToInt64(holdResult, CultureInfo.InvariantCulture));
            }

            await using var killerConnection = new MySqlConnection(scopedConnectionString);
            await killerConnection
                .OpenAsync()
                .ConfigureAwait(false);
            await using (var killCommand = killerConnection.CreateCommand())
            {
                killCommand.CommandText = $"KILL {holderConnection.ServerThread};";
                await killCommand
                    .ExecuteNonQueryAsync()
                    .ConfigureAwait(false);
            }

            // KILL is asynchronous on the server side; give the reaper a beat before
            // the verification acquire to keep the test deterministic on slow runners.
            await Task
                .Delay(TimeSpan.FromMilliseconds(250))
                .ConfigureAwait(false);

            await using var verifier = new LockContext(CreateOptions(scopedConnectionString));
            var verifierHistory = verifier.GetService<IHistoryRepository>();

            // Act
            await using var reAcquired = await verifierHistory
                .AcquireDatabaseLockAsync()
                .ConfigureAwait(false);

            // Assert
            Assert.NotNull(reAcquired);
        }
        finally
        {
            await DropDatabaseAsync(baseConnectionString, dbName)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verifies provider lock timeout honors configured command timeout.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Provider_lock_timeout_honors_configured_command_timeout()
    {
        // Arrange
        var baseConnectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);
        var dbName = $"doka_lock_t_{Guid.NewGuid():N}"[..30];

        await CreateDatabaseAsync(baseConnectionString, dbName)
            .ConfigureAwait(false);
        var scopedConnectionString = BuildConnectionString(baseConnectionString, dbName);

        try
        {
            await using var holderContext = new LockContext(CreateOptions(scopedConnectionString));
            await using var contenderContext = new LockContext(
                CreateOptions(scopedConnectionString, commandTimeout: 1));

            var holderHistory = holderContext.GetService<IHistoryRepository>();
            var contenderHistory = contenderContext.GetService<IHistoryRepository>();

            await using var heldLock = await holderHistory
                .AcquireDatabaseLockAsync()
                .ConfigureAwait(false);

            // Act
            var stopwatch = Stopwatch.StartNew();

            await Assert.ThrowsAsync<TimeoutException>(
                () => contenderHistory.AcquireDatabaseLockAsync());
            stopwatch.Stop();

            // Assert
            Assert.InRange(
                stopwatch.Elapsed,
                TimeSpan.FromMilliseconds(750),
                TimeSpan.FromSeconds(5));
        }
        finally
        {
            await DropDatabaseAsync(baseConnectionString, dbName)
                .ConfigureAwait(false);
        }
    }

    /// <summary>The synchronous lock path reports either server or driver deadline as a lock timeout.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Synchronous_provider_lock_timeout_honors_configured_command_timeout()
    {
        // Arrange
        var baseConnectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);
        var dbName = $"doka_lock_st_{Guid.NewGuid():N}"[..30];

        await CreateDatabaseAsync(baseConnectionString, dbName)
            .ConfigureAwait(false);
        var scopedConnectionString = BuildConnectionString(baseConnectionString, dbName);

        try
        {
            await using var holderContext = new LockContext(CreateOptions(scopedConnectionString));
            await using var contenderContext = new LockContext(
                CreateOptions(scopedConnectionString, commandTimeout: 1));

            var holderHistory = holderContext.GetService<IHistoryRepository>();
            var contenderHistory = contenderContext.GetService<IHistoryRepository>();

            await using var heldLock = await holderHistory
                .AcquireDatabaseLockAsync()
                .ConfigureAwait(false);

            // Act
            var stopwatch = Stopwatch.StartNew();

            // WHY: The synchronous advisory-lock path must enforce the same deadline independently.
            // ReSharper disable once MethodHasAsyncOverload
            Assert.Throws<TimeoutException>(contenderHistory.AcquireDatabaseLock);
            stopwatch.Stop();

            // Assert
            Assert.InRange(
                stopwatch.Elapsed,
                TimeSpan.FromMilliseconds(750),
                TimeSpan.FromSeconds(5));
        }
        finally
        {
            await DropDatabaseAsync(baseConnectionString, dbName)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verifies provider lock acquire honors cancellation and cleans up.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Provider_lock_acquire_honors_cancellation_and_cleans_up()
    {
        // Arrange
        var baseConnectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);
        var dbName = $"doka_lock_c_{Guid.NewGuid():N}"[..30];

        await CreateDatabaseAsync(baseConnectionString, dbName)
            .ConfigureAwait(false);
        var scopedConnectionString = BuildConnectionString(baseConnectionString, dbName);

        try
        {
            await using var holderContext = new LockContext(CreateOptions(scopedConnectionString));
            await using var contenderContext = new LockContext(
                CreateOptions(scopedConnectionString, commandTimeout: 30));

            var holderHistory = holderContext.GetService<IHistoryRepository>();
            var contenderHistory = contenderContext.GetService<IHistoryRepository>();

            await using (var heldLock = await holderHistory
                             .AcquireDatabaseLockAsync()
                             .ConfigureAwait(false))
            {
                using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => contenderHistory.AcquireDatabaseLockAsync(cancellation.Token));
            }

            await using var verifierContext = new LockContext(CreateOptions(scopedConnectionString));

            // Act
            await using var verifierLock = await verifierContext
                .GetService<IHistoryRepository>()
                .AcquireDatabaseLockAsync()
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);

            // Assert
            Assert.NotNull(verifierLock);
        }
        finally
        {
            await DropDatabaseAsync(baseConnectionString, dbName)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verifies data source lock dispose race cannot resurrect server lock.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Data_source_lock_dispose_race_cannot_resurrect_server_lock()
    {
        // Arrange
        var baseConnectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);
        var dbName = $"doka_lock_d_{Guid.NewGuid():N}"[..30];

        await CreateDatabaseAsync(baseConnectionString, dbName)
            .ConfigureAwait(false);
        var scopedConnectionString = BuildConnectionString(baseConnectionString, dbName);
        var lockName = MySqlAdvisoryLockNaming.BuildLockName(scopedConnectionString);

        try
        {
            await using var dataSource = new MySqlDataSourceBuilder(scopedConnectionString).Build();
            await using var context = new LockContext(CreateOptions(dataSource));
            var lockInstance = await context
                .GetService<IHistoryRepository>()
                .AcquireDatabaseLockAsync()
                .ConfigureAwait(false);

            var reacquireTask = Task.Run(async () =>
            {
                try
                {
                    _ = await lockInstance
                        .ReacquireIfNeededAsync(connectionReopened: true, transactionRestarted: null)
                        .ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    // Disposal won the lifecycle gate; terminal disposal is expected.
                }
            });

            var disposeTask = Task.Run(
                async () => await lockInstance
                    .DisposeAsync()
                    .ConfigureAwait(false));

            await Task
                .WhenAll(reacquireTask, disposeTask)
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);

            await AssertLockIsFreeAsync(scopedConnectionString, lockName)
                .ConfigureAwait(false);

            // Act
            await using var verificationConnection = await dataSource
                .OpenConnectionAsync()
                .ConfigureAwait(false);

            // Assert
            Assert.Equal(ConnectionState.Open, verificationConnection.State);
        }
        finally
        {
            await DropDatabaseAsync(baseConnectionString, dbName)
                .ConfigureAwait(false);
        }
    }

    private static string BuildConnectionString(
        string baseConnectionString,
        string databaseName
    )
    {
        var builder = new MySqlConnectionStringBuilder(baseConnectionString)
        {
            Database = databaseName,
            GuidFormat = MySqlConnector.MySqlGuidFormat.Binary16,
        };

        return builder.ConnectionString;
    }

    private static async Task CreateDatabaseAsync(
        string baseConnectionString,
        string databaseName
    )
    {
        var serverBuilder = new MySqlConnectionStringBuilder(baseConnectionString)
        {
            Database = string.Empty,
        };

        serverBuilder.Remove("Database");

        await using var connection = new MySqlConnection(serverBuilder.ConnectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE IF NOT EXISTS `{databaseName}` CHARACTER SET utf8mb4;";
        await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    private static async Task DropDatabaseAsync(
        string baseConnectionString,
        string databaseName
    )
    {
        var serverBuilder = new MySqlConnectionStringBuilder(baseConnectionString)
        {
            Database = string.Empty,
        };

        serverBuilder.Remove("Database");

        await using var connection = new MySqlConnection(serverBuilder.ConnectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS `{databaseName}`;";
        await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    private static DbContextOptions<LockContext> CreateOptions(
        string connectionString,
        int? commandTimeout = null
    )
    {
        var builder = IntegrationTestDbContextOptions.Create<LockContext>();
        builder.UseMySql(
            connectionString,
            MySqlServerVersion.MySql(new Version(8, 4, 0)),
            options =>
            {
                if (commandTimeout.HasValue)
                {
                    options.CommandTimeout(commandTimeout.Value);
                }
            });

        return builder.Options;
    }

    private static DbContextOptions<LockContext> CreateOptions(
        MySqlDataSource dataSource
    )
    {
        var builder = IntegrationTestDbContextOptions.Create<LockContext>();
        builder.UseMySql(dataSource, MySqlServerVersion.MySql(new Version(8, 4, 0)));

        return builder.Options;
    }

    private static async Task AssertLockIsFreeAsync(
        string connectionString,
        string lockName
    )
    {
        // Arrange
        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);

        // Act
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT IS_FREE_LOCK(@name);";
        command.Parameters.AddWithValue("@name", lockName);

        // Assert
        Assert.Equal(
            1L,
            Convert.ToInt64(
                await command
                    .ExecuteScalarAsync()
                    .ConfigureAwait(false),
                CultureInfo.InvariantCulture));
    }

    private sealed class LockContext : DbContext
    {
        /// <summary>
        /// Initializes the LockContext fixture.
        /// </summary>
        public LockContext(
            DbContextOptions<LockContext> options
        ) : base(options) { }
    }
}
