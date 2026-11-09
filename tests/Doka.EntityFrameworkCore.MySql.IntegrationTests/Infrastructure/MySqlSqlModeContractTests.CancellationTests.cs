namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Exercises observer failures and native binary statements against owned cancellation probes.</summary>
public sealed partial class MySqlSqlModeContractTests
{
    /// <summary>Returns each selected supported engine independently.</summary>
    public static TheoryData<string> CancellationTargets
    {
        get
        {
            var cases = new TheoryData<string>();

            foreach (var target in IntegrationTestEnvironment.GetSelectedTargets())
            {
                if (IntegrationTestEnvironment
                    .GetSupportedTargets()
                    .Contains(target))
                {
                    cases.Add(target.ToString());
                }
            }

            return cases;
        }
    }

    /// <summary>Reports ordinary observer failures and canceled observations as separate cases.</summary>
    public static TheoryData<string, bool> CancellationObserverFailures
    {
        get
        {
            var cases = new TheoryData<string, bool>();

            foreach (var target in IntegrationTestEnvironment.GetSelectedTargets())
            {
                if (!IntegrationTestEnvironment
                    .GetSupportedTargets()
                    .Contains(target))
                {
                    continue;
                }

                cases.Add(target.ToString(), false);
                cases.Add(target.ToString(), true);
            }

            return cases;
        }
    }

    /// <summary>An observer failure remains authoritative while its migration is canceled, drained, and removed.</summary>
    /// <param name="targetName">The selected supported engine.</param>
    /// <param name="canceledObservation">Whether the primary failure represents observation cancellation.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(CancellationObserverFailures))]
    public async Task Cancellation_probe_preserves_observer_failure_and_restores_connection(
        string targetName,
        bool canceledObservation
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);
        var connectionString = CreateSqlModeConnectionString(target);
        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync(CancellationToken.None)
            .ConfigureAwait(false);

        await using var context = new SqlModeContractContext(
            IntegrationTestDbContextOptions
                .Create<SqlModeContractContext>()
                .UseMySql(connection, IntegrationTestEnvironment.GetServerVersion(target))
                .Options);

        Exception expected = canceledObservation
            ? new OperationCanceledException("Controlled metadata observer cancellation.", new CancellationToken(true))
            : new InvalidOperationException("Controlled metadata observer failure.");

        string? restoredMode = null;
        long remainingTables = -1;

        // Act
        var failure = await Record.ExceptionAsync(() => AssertCanceledMigrationRestoresSessionAsync(
            connectionString,
            connection,
            context,
            async (observer, connectionId, token) =>
            {
                await WaitForMetadataLockAsync(observer, connectionId, token)
                    .ConfigureAwait(false);

                throw expected;
            },
            CancellationToken.None));

        var reuseFailure = await Record.ExceptionAsync(async () =>
        {
            restoredMode = await ReadSqlModeAsync(connection, CancellationToken.None)
                .ConfigureAwait(false);

            remainingTables = await CountCancellationProbeTablesAsync(connection, CancellationToken.None)
                .ConfigureAwait(false);
        });

        // Assert
        Assert.Same(expected, failure);
        Assert.Contains(
            nameof(Cancellation_probe_preserves_observer_failure_and_restores_connection),
            expected.StackTrace);

        Assert.Null(reuseFailure);
        Assert.Equal("ANSI_QUOTES,STRICT_TRANS_TABLES", restoredMode);
        Assert.Equal(0L, remainingTables);
        var secondary = Assert.IsType<AggregateException>(
            expected.Data[nameof(DatabaseLockProbeCleanup.CompleteLockProbeAsync)]);

        Assert.Contains(secondary.InnerExceptions, exception => exception is OperationCanceledException);
        Assert.DoesNotContain(secondary.InnerExceptions, exception => exception.Message.Contains("already in use"));
    }

    /// <summary>Unrelated real binary SQL cannot break STATE-only metadata observation and cancellation recovery.</summary>
    /// <param name="targetName">The selected supported engine.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(CancellationTargets))]
    public async Task Cancellation_probe_survives_unrelated_native_binary_statement(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);
        var connectionString = CreateSqlModeConnectionString(target);
        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync(CancellationToken.None)
            .ConfigureAwait(false);

        await using var context = new SqlModeContractContext(
            IntegrationTestDbContextOptions
                .Create<SqlModeContractContext>()
                .UseMySql(connection, IntegrationTestEnvironment.GetServerVersion(target))
                .Options);

        var observedBinaryStatement = false;

        // Act
        var failure = await Record.ExceptionAsync(() => AssertCanceledMigrationRestoresSessionAsync(
            connectionString,
            connection,
            context,
            (observer, connectionId, token) => WithUnrelatedBinaryStatementAsync(
                connectionString,
                observer,
                async observationToken =>
                {
                    await WaitForMetadataLockAsync(observer, connectionId, observationToken)
                        .ConfigureAwait(false);

                    observedBinaryStatement = true;
                },
                token),
            CancellationToken.None));

        var remainingTables = await CountCancellationProbeTablesAsync(connection, CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        Assert.Null(failure);
        Assert.True(observedBinaryStatement);
        Assert.Equal(0L, remainingTables);
    }

    /// <summary>Another waiting session cannot make an idle or absent session match the observer.</summary>
    /// <param name="targetName">The selected supported engine.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(CancellationTargets))]
    public async Task Metadata_observer_matches_only_the_requested_waiting_session(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);
        var connectionString = CreateSqlModeConnectionString(target);
        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync(CancellationToken.None)
            .ConfigureAwait(false);

        await using var context = new SqlModeContractContext(
            IntegrationTestDbContextOptions
                .Create<SqlModeContractContext>()
                .UseMySql(connection, IntegrationTestEnvironment.GetServerVersion(target))
                .Options);

        var waitingSessionMatched = false;
        var observerSessionMatched = true;
        var absentSessionMatched = true;

        // Act
        await AssertCanceledMigrationRestoresSessionAsync(
            connectionString,
            connection,
            context,
            async (observer, connectionId, token) =>
            {
                await WaitForMetadataLockAsync(observer, connectionId, token)
                    .ConfigureAwait(false);

                waitingSessionMatched = await IsWaitingForMetadataLockAsync(observer, connectionId, token)
                    .ConfigureAwait(false);

                observerSessionMatched = await IsWaitingForMetadataLockAsync(observer, observer.ServerThread, token)
                    .ConfigureAwait(false);

                absentSessionMatched = await IsWaitingForMetadataLockAsync(observer, int.MaxValue, token)
                    .ConfigureAwait(false);
            },
            CancellationToken.None);

        // Assert
        Assert.True(waitingSessionMatched);
        Assert.False(observerSessionMatched);
        Assert.False(absentSessionMatched);
    }

    /// <summary>A canceled observation never starts a new query or leaves the observer session unusable.</summary>
    /// <param name="targetName">The selected supported engine.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(CancellationTargets))]
    public async Task Metadata_observer_honors_cancellation_and_retains_connection_reuse(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);
        await using var observer = new MySqlConnection(CreateSqlModeConnectionString(target));
        await observer
            .OpenAsync(CancellationToken.None)
            .ConfigureAwait(false);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        var failure = await Record.ExceptionAsync(() => IsWaitingForMetadataLockAsync(
            observer,
            observer.ServerThread,
            cancellation.Token));

        var mode = await ReadSqlModeAsync(observer, CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        var canceled = Assert.IsAssignableFrom<OperationCanceledException>(failure);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.NotNull(mode);
    }

    /// <summary>Owns a real blocked binary UPDATE while another session's STATE is observed.</summary>
    private static async Task WithUnrelatedBinaryStatementAsync(
        string connectionString,
        MySqlConnection observer,
        Func<CancellationToken, Task> observeMigration,
        CancellationToken cancellationToken
    )
    {
        var tableName = $"SqlModeBinary_{Guid.NewGuid():N}";
        await using var blocker = new MySqlConnection(connectionString);
        await using var worker = new MySqlConnection(connectionString);
        await blocker
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await worker
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await using (var create = blocker.CreateCommand())
        {
            create.CommandText = $"CREATE TABLE `{tableName}` (`Id` int PRIMARY KEY, `Value` varbinary(5)); "
                + $"INSERT INTO `{tableName}` VALUES (1, 0x00);";

            _ = await create
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        await using var transaction = await blocker
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using (var acquire = new MySqlCommand(
            $"SELECT `Id` FROM `{tableName}` WHERE `Id` = 1 FOR UPDATE;",
            blocker,
            transaction))
        {
            _ = await acquire
                .ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        await using var update = worker.CreateCommand();
        update.CommandText = $"UPDATE `{tableName}` SET `Value` = @value WHERE `Id` = 1;";

        // WHY: Invalid UTF-8 bytes must be present in native server process metadata, not a synthetic SQL value.
        update.Parameters.Add("@value", MySqlDbType.VarBinary)
            .Value = new byte[]
        {
            0x80,
            0,
            0xFF,
            0x88,
            0x99,
        };

        var pending = update.ExecuteNonQueryAsync(cancellationToken);
        Exception? primaryFailure = null;

        try
        {
            await WaitForNativeBinaryCommandAsync(observer, worker.ServerThread, pending, cancellationToken)
                .ConfigureAwait(false);

            await observeMigration(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            primaryFailure = failure;

            throw;
        }
        finally
        {
            await DatabaseLockProbeCleanup
                .CompleteLockProbeAsync(
                    cancellation: null,

                    // WHY: A physical close rolls back and releases the row even when explicit rollback would fail.
                    blocker.CloseAsync,
                    pending,
                    primaryFailure,
                    () => DropSqlModeProbeTableAsync(observer, tableName))
                .ConfigureAwait(false);
        }
    }

    /// <summary>Confirms the binary worker reached the server before the subject observation is exercised.</summary>
    private static async Task WaitForNativeBinaryCommandAsync(
        MySqlConnection observer,
        int workerId,
        Task pending,
        CancellationToken cancellationToken
    )
    {
        var source = GetMetadataProcessSource(observer);

        await using var command = observer.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {source} WHERE ID = @workerId AND COMMAND = 'Query';";
        command.Parameters.AddWithValue("@workerId", workerId);
        var timeout = Stopwatch.StartNew();

        while (!pending.IsCompleted
            && timeout.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (Convert.ToInt64(
                    await command
                        .ExecuteScalarAsync(cancellationToken)
                        .ConfigureAwait(false),
                    CultureInfo.InvariantCulture)
                == 1)
            {
                return;
            }

            await Task
                .Delay(25, cancellationToken)
                .ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            "The native binary statement did not remain blocked in its server session.");
    }

    /// <summary>Checks that cancellation cleanup left no owned probe table behind using the reusable caller session.</summary>
    private static async Task<long> CountCancellationProbeTablesAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM information_schema.TABLES "
            + "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME LIKE 'SqlModeCancellation%';";

        var count = await command
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);

        return Convert.ToInt64(count, CultureInfo.InvariantCulture);
    }
}
