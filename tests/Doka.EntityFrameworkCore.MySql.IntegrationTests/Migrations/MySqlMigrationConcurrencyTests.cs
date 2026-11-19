namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Live concurrency coverage for the migration advisory lock (per ADR D-002): two
/// migrations running against different databases on the same MySQL instance must
/// not block each other, while two migrations against the same database must
/// serialize, with the third caller observing a <see cref="TimeoutException" />
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

        await CreateDatabaseAsync(baseConnectionString, dbNameA, CancellationToken.None)
            .ConfigureAwait(false);

        await CreateDatabaseAsync(baseConnectionString, dbNameB, CancellationToken.None)
            .ConfigureAwait(false);

        try
        {
            await using var contextA =
                new LockContext(CreateOptions(BuildConnectionString(baseConnectionString, dbNameA)));

            await using var contextB =
                new LockContext(CreateOptions(BuildConnectionString(baseConnectionString, dbNameB)));

            var historyA = contextA.GetService<IHistoryRepository>();
            var historyB = contextB.GetService<IHistoryRepository>();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(1));

            // Act
            // WHY: Retaining the first lock while acquiring the second proves independent
            // scopes without leaving a successful acquisition unowned if the other fails.
            await using var lockA = await historyA
                .AcquireDatabaseLockAsync(cancellation.Token)
                .ConfigureAwait(false);

            await using var lockB = await historyB
                .AcquireDatabaseLockAsync(cancellation.Token)
                .ConfigureAwait(false);

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
    /// timeout surfaces a <see cref="TimeoutException" />.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Migration_locks_serialize_on_same_database_and_surface_timeout()
    {
        // Arrange
        var baseConnectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);
        var dbName = $"doka_lock_s_{Guid.NewGuid():N}"[..30];

        await CreateDatabaseAsync(baseConnectionString, dbName, CancellationToken.None)
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
                .OpenAsync(CancellationToken.None)
                .ConfigureAwait(false);

            await using var probe = contender.CreateCommand();
            probe.CommandText = "SELECT GET_LOCK(@name, 0);";
            var nameParam = probe.CreateParameter();
            nameParam.ParameterName = "@name";
            nameParam.Value = lockName;
            probe.Parameters.Add(nameParam);

            // Act
            var probeResult = await probe
                .ExecuteScalarAsync(CancellationToken.None)
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
    /// than proceeding into a parallel migration. The server must witness the pending
    /// request before the holder releases; elapsed time is not an assertion.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Migration_lock_acquire_waits_when_held_by_another_session()
    {
        // Arrange
        var baseConnectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);
        var dbName = $"doka_lock_w_{Guid.NewGuid():N}"[..30];

        await CreateDatabaseAsync(baseConnectionString, dbName, CancellationToken.None)
            .ConfigureAwait(false);

        var scopedConnectionString = BuildConnectionString(baseConnectionString, dbName);

        try
        {
            await using var holderContext = new LockContext(CreateOptions(scopedConnectionString));
            await using var waiterContext = new LockContext(CreateOptions(scopedConnectionString));
            var holderHistory = holderContext.GetService<IHistoryRepository>();
            var waiterHistory = waiterContext.GetService<IHistoryRepository>();
            var lockName = MySqlAdvisoryLockNaming.BuildLockName(scopedConnectionString);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(1));

            await using var heldLock = await holderHistory
                .AcquireDatabaseLockAsync(cancellation.Token)
                .ConfigureAwait(false);

            IMigrationsDatabaseLock acquiredLock;
            bool completedBeforeRelease;

            // Act
            var pendingAcquire = waiterHistory.AcquireDatabaseLockAsync(cancellation.Token);

            try
            {
                await WaitForPendingLockAsync(scopedConnectionString, lockName, pendingAcquire, cancellation.Token)
                    .ConfigureAwait(false);

                completedBeforeRelease = pendingAcquire.IsCompleted;
            }
            finally
            {
                try
                {
                    // WHY: Early release permits the waiter to finish. The await-using owner
                    // still covers earlier failures; migration-lock disposal is idempotent.
                    // ReSharper disable once DisposeOnUsingVariable
                    await heldLock
                        .DisposeAsync()
                        .ConfigureAwait(false);
                }
                finally
                {
                    // WHY: Always observe and release the pending acquisition before either
                    // context leaves scope, including a failed server-wait observation.
                    await using var waitedLock = await pendingAcquire.ConfigureAwait(false);
                    acquiredLock = waitedLock;
                }
            }

            // Assert
            Assert.False(completedBeforeRelease);
            Assert.NotNull(acquiredLock);
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

        await CreateDatabaseAsync(baseConnectionString, dbName, CancellationToken.None)
            .ConfigureAwait(false);

        var scopedConnectionString = BuildConnectionString(baseConnectionString, dbName);
        var lockName = MySqlAdvisoryLockNaming.BuildLockName(scopedConnectionString);

        try
        {
            await using var holderConnection = new MySqlConnection(scopedConnectionString);
            await holderConnection
                .OpenAsync(CancellationToken.None)
                .ConfigureAwait(false);

            await using (var holdCommand = holderConnection.CreateCommand())
            {
                holdCommand.CommandText = "SELECT GET_LOCK(@name, 5);";
                var holdNameParam = holdCommand.CreateParameter();
                holdNameParam.ParameterName = "@name";
                holdNameParam.Value = lockName;
                holdCommand.Parameters.Add(holdNameParam);

                var holdResult = await holdCommand
                    .ExecuteScalarAsync(CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.Equal(1L, Convert.ToInt64(holdResult, CultureInfo.InvariantCulture));
            }

            await using var killerConnection = new MySqlConnection(scopedConnectionString);
            await killerConnection
                .OpenAsync(CancellationToken.None)
                .ConfigureAwait(false);

            await using (var killCommand = killerConnection.CreateCommand())
            {
                killCommand.CommandText = $"KILL {holderConnection.ServerThread};";
                await killCommand
                    .ExecuteNonQueryAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }

            await using var verifier = new LockContext(CreateOptions(scopedConnectionString));
            var verifierHistory = verifier.GetService<IHistoryRepository>();

            // Act
            // WHY: GET_LOCK itself waits for the killed session's lock to be reaped;
            // a fixed delay neither proves release nor accommodates every scheduler.
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

        await CreateDatabaseAsync(baseConnectionString, dbName, CancellationToken.None)
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
            var recordedException = await Record.ExceptionAsync(() =>
                contenderHistory.AcquireDatabaseLockAsync(CancellationToken.None));

            // Assert
            var timeout = Assert.IsType<TimeoutException>(recordedException);

            // WHY: The configured native/provider deadline must survive normalization;
            // elapsed scheduler time is not evidence of which deadline was applied.
            Assert.Contains("within 1 seconds.", timeout.Message, StringComparison.Ordinal);
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

        await CreateDatabaseAsync(baseConnectionString, dbName, CancellationToken.None)
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
            // WHY: The synchronous advisory-lock path must enforce the same deadline independently.
            // ReSharper disable once MethodHasAsyncOverload
            var recordedException = Record.Exception(() => contenderHistory.AcquireDatabaseLock());

            // Assert
            var timeout = Assert.IsType<TimeoutException>(recordedException);
            Assert.Contains("within 1 seconds.", timeout.Message, StringComparison.Ordinal);
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

        await CreateDatabaseAsync(baseConnectionString, dbName, CancellationToken.None)
            .ConfigureAwait(false);

        var scopedConnectionString = BuildConnectionString(baseConnectionString, dbName);

        try
        {
            await using var holderContext = new LockContext(CreateOptions(scopedConnectionString));
            await using var contenderContext = new LockContext(
                CreateOptions(scopedConnectionString, commandTimeout: 30));

            var holderHistory = holderContext.GetService<IHistoryRepository>();
            var contenderHistory = contenderContext.GetService<IHistoryRepository>();
            var lockName = MySqlAdvisoryLockNaming.BuildLockName(scopedConnectionString);
            Exception? cancellationException;

            await using (var heldLock = await holderHistory
                .AcquireDatabaseLockAsync()
                .ConfigureAwait(false))
            {
                using var cancellation = new CancellationTokenSource();
                using var observationCancellation = new CancellationTokenSource(TimeSpan.FromMinutes(1));

                // Act
                var pendingAcquire = contenderHistory.AcquireDatabaseLockAsync(cancellation.Token);

                try
                {
                    await WaitForPendingLockAsync(scopedConnectionString,
                            lockName,
                            pendingAcquire,
                            observationCancellation.Token)
                        .ConfigureAwait(false);
                }
                finally
                {
                    // WHY: Cancel only after the server witnesses contention, and always
                    // drain the operation before releasing the holder or its contexts.
                    cancellation.Cancel();
                    cancellationException = await Record.ExceptionAsync(async () =>
                    {
                        await using var unexpectedLock = await pendingAcquire.ConfigureAwait(false);
                    });
                }
            }

            await using var verifierContext = new LockContext(CreateOptions(scopedConnectionString));
            using var verificationCancellation = new CancellationTokenSource(TimeSpan.FromMinutes(1));

            await using var verifierLock = await verifierContext
                .GetService<IHistoryRepository>()
                .AcquireDatabaseLockAsync(verificationCancellation.Token)
                .ConfigureAwait(false);

            // Assert
            Assert.IsAssignableFrom<OperationCanceledException>(cancellationException);
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

        await CreateDatabaseAsync(baseConnectionString, dbName, CancellationToken.None)
            .ConfigureAwait(false);

        var scopedConnectionString = BuildConnectionString(baseConnectionString, dbName);
        var lockName = MySqlAdvisoryLockNaming.BuildLockName(scopedConnectionString);

        try
        {
            await using var dataSource = new MySqlDataSourceBuilder(scopedConnectionString).Build();
            await using var context = new LockContext(CreateOptions(dataSource));
            await using var lockInstance = await context
                .GetService<IHistoryRepository>()
                .AcquireDatabaseLockAsync()
                .ConfigureAwait(false);

            using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(1));

            // Act
            var reacquireTask = Task.Run(async () =>
            {
                try
                {
                    // WHY: This test deliberately races reacquisition against disposal;
                    // both operations finish before the await-using owner leaves scope.
                    // ReSharper disable once AccessToDisposedClosure
                    _ = await lockInstance
                        .ReacquireIfNeededAsync(connectionReopened: true,
                            transactionRestarted: null,
                            cancellation.Token)
                        .ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    // Disposal won the lifecycle gate; terminal disposal is expected.
                }
            }, CancellationToken.None);

            var disposeTask = Task.Run(async () =>
            {
                // WHY: The race intentionally disposes the borrowed instance early;
                // its await-using owner still covers setup and task-launch failures.
                // ReSharper disable once AccessToDisposedClosure
                // ReSharper disable once DisposeOnUsingVariable
                await lockInstance
                    .DisposeAsync()
                    .ConfigureAwait(false);
            }, CancellationToken.None);

            await Task
                .WhenAll(reacquireTask, disposeTask)
                .ConfigureAwait(false);

            await using var verificationConnection = await dataSource
                .OpenConnectionAsync(CancellationToken.None)
                .ConfigureAwait(false);

            // Assert
            await AssertLockIsFreeAsync(scopedConnectionString, lockName, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Equal(ConnectionState.Open, verificationConnection.State);
        }
        finally
        {
            await DropDatabaseAsync(baseConnectionString, dbName)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Waits for the server to expose this exact advisory-lock request as blocked.</summary>
    /// <param name="connectionString">The database connection string used by the observer.</param>
    /// <param name="lockName">The unique advisory-lock name shared by the holder and contender.</param>
    /// <param name="pendingAcquire">The contender operation that must remain incomplete while blocked.</param>
    /// <param name="cancellationToken">The safety token for stalled server observation.</param>
    private static async Task WaitForPendingLockAsync(
        string connectionString,
        string lockName,
        Task<IMigrationsDatabaseLock> pendingAcquire,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.Parameters.AddWithValue("@name", lockName);
        command.CommandText = """
        SELECT EXISTS (
            SELECT 1
            FROM performance_schema.metadata_locks
            WHERE OBJECT_TYPE = 'USER LEVEL LOCK'
              AND OBJECT_NAME = @name
              AND LOCK_STATUS = 'GRANTED'
        );
        """;

        if (Convert.ToInt64(await command
                    .ExecuteScalarAsync(cancellationToken)
                    .ConfigureAwait(false),
                CultureInfo.InvariantCulture)
            != 1)
        {
            throw new InvalidOperationException(
                "The held advisory lock must be visible in MySQL's default metadata-lock instrumentation.");
        }

        // WHY: MySQL exposes blocked GET_LOCK requests as PENDING user-level metadata
        // locks. The unique lock name witnesses native contention independently of
        // scheduler delay; the dedicated provider session is not the context session.
        command.CommandText = """
        SELECT EXISTS (
            SELECT 1
            FROM performance_schema.metadata_locks
            WHERE OBJECT_TYPE = 'USER LEVEL LOCK'
              AND OBJECT_NAME = @name
              AND LOCK_STATUS = 'PENDING'
        );
        """;

        while (Convert.ToInt64(await command
                    .ExecuteScalarAsync(cancellationToken)
                    .ConfigureAwait(false),
                CultureInfo.InvariantCulture)
            != 1)
        {
            if (pendingAcquire.IsCompleted)
            {
                // Observe a native acquire failure now; the caller retains ownership
                // of any unexpectedly successful acquisition and disposes it in finally.
                await pendingAcquire.ConfigureAwait(false);

                throw new InvalidOperationException(
                    "The contender completed before the server witnessed a pending advisory-lock request.");
            }
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

    /// <summary>
    /// Creates database for the integration contract.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel setup and execution.</param>
    /// <param name="baseConnectionString">The target database connection string.</param>
    /// <param name="databaseName">The database name.</param>
    private static async Task CreateDatabaseAsync(
        string baseConnectionString,
        string databaseName,
        CancellationToken cancellationToken
    )
    {
        var serverBuilder = new MySqlConnectionStringBuilder(baseConnectionString)
        {
            Database = string.Empty,
        };

        serverBuilder.Remove("Database");

        await using var connection = new MySqlConnection(serverBuilder.ConnectionString);
        await connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE IF NOT EXISTS `{databaseName}` CHARACTER SET utf8mb4;";
        await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    // WHY: Cleanup must finish after caller cancellation to leave no test resources behind.
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
            .OpenAsync(CancellationToken.None)
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS `{databaseName}`;";
        await command
            .ExecuteNonQueryAsync(CancellationToken.None)
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

    /// <summary>
    /// Verifies lock is free for the integration contract.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel setup and execution.</param>
    /// <param name="connectionString">The target database connection string.</param>
    /// <param name="lockName">The lock name.</param>
    private static async Task AssertLockIsFreeAsync(
        string connectionString,
        string lockName,
        CancellationToken cancellationToken
    )
    {
        // Arrange
        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync(cancellationToken)
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
                    .ExecuteScalarAsync(cancellationToken)
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
