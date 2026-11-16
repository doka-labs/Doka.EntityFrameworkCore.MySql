namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Verifies process observers against real sessions containing binary command parameters.</summary>
public sealed partial class MySqlDistributedCacheIntegrationTests
{
    /// <summary>Returns native row/table waits and independent false-match cases for every selected engine.</summary>
    public static TheoryData<string, bool, bool, bool, long> NativeLockProbeCases
    {
        get
        {
            var cases = new TheoryData<string, bool, bool, bool, long>();
            var targets = IntegrationTestEnvironment.GetSelectedTargets();

            // WHY: Engine selection is stable during discovery; index the existing collection without materializing it.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < targets.Count; index++)
            {
                if (!IntegrationTestEnvironment
                    .GetSupportedTargets()
                    .Contains(targets[index]))
                {
                    continue;
                }

                var targetName = targets[index]
                    .ToString();

                cases.Add(targetName, false, true, false, 1L);
                cases.Add(targetName, false, false, false, 0L);
                cases.Add(targetName, false, true, true, 0L);
                cases.Add(targetName, true, true, false, 1L);
                cases.Add(targetName, true, false, false, 0L);
            }

            return cases;
        }
    }

    /// <summary>Rejects missing, previous-command and premature table-lock metadata independently of the matcher.</summary>
    /// <param name="statement">The statement bytes available in the native snapshot.</param>
    /// <param name="tableLock">Whether a native table-lock state is required.</param>
    /// <param name="state">The unmodified native process state.</param>
    /// <param name="expected">Whether this snapshot belongs to the current blocked command.</param>
    [Theory]
    [InlineData(null, false, null, false)]
    [InlineData("", false, null, false)]
    [InlineData("UPDATE /*previous-native-probe*/", false, "Updating", false)]
    [InlineData("UPDATE /*current-native-probe*/", false, "Updating", true)]
    [InlineData("UPDATE /*current-native-probe*/", true, null, false)]
    [InlineData("UPDATE /*current-native-probe*/", true, "Updating", false)]
    [InlineData("UPDATE /*current-native-probe*/", true, "Waiting for table metadata lock", true)]
    [InlineData("UPDATE /*current-native-probe*/", true, "WAITING FOR TABLE LEVEL LOCK", true)]
    [InlineData("UPDATE /*previous-native-probe*/", true, "Waiting for table metadata lock", false)]
    public void Native_process_snapshot_requires_the_current_statement(
        string? statement,
        bool tableLock,
        string? state,
        bool expected
    )
    {
        // Arrange
        var statementBytes = statement is null ? null : Encoding.ASCII.GetBytes(statement);

        // Act
        var actual = IsCurrentNativeProbeSnapshot(statementBytes, "current-native-probe"u8, tableLock, state);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>Verifies native binary statements neither break process observation nor create false matches.</summary>
    /// <param name="targetName">The selected engine for this independently reported case.</param>
    /// <param name="tableLock">Whether the UPDATE waits for a table rather than a row lock.</param>
    /// <param name="matchingTable">Whether the observer searches for the actual table.</param>
    /// <param name="cleanupOnly">Whether a foreground UPDATE must be excluded from cleanup observation.</param>
    /// <param name="expectedMatches">The number of eligible native sessions.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(NativeLockProbeCases))]
    public async Task Native_lock_probes_handle_binary_statements_without_false_matches(
        string targetName,
        bool tableLock,
        bool matchingTable,
        bool cleanupOnly,
        long expectedMatches
    )
    {
        // Arrange
        var target = ParseTarget(targetName);
        await using var store = await CreateDeployedStoreAsync(target, CancellationToken.None)
            .ConfigureAwait(false);

        await store
            .Cache
            .SetAsync("native-probe", [1], new DistributedCacheEntryOptions(), CancellationToken.None)
            .ConfigureAwait(false);

        await using var blocker = new MySqlConnection(store.ConnectionString);
        await using var worker = new MySqlConnection(store.ConnectionString);
        await using var observer = new MySqlConnection(store.ConnectionString);

        await blocker
            .OpenAsync(CancellationToken.None)
            .ConfigureAwait(false);

        await worker
            .OpenAsync(CancellationToken.None)
            .ConfigureAwait(false);

        await observer
            .OpenAsync(CancellationToken.None)
            .ConfigureAwait(false);

        await using var transaction = tableLock
            ? null
            : await blocker
                .BeginTransactionAsync(CancellationToken.None)
                .ConfigureAwait(false);

        await using (var acquire = new MySqlCommand(
            tableLock
                ? $"LOCK TABLES {store.QualifiedTableName} WRITE;"
                : $"SELECT `Revision` FROM {store.QualifiedTableName} "
                + "WHERE `Id` = CAST('native-probe' AS BINARY) FOR UPDATE;",
            blocker,
            transaction))
        {
            await acquire
                .ExecuteScalarAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }

        var statementMarker = $"doka-native-probe:{Guid.NewGuid():N}";
        var markerBytes = Encoding.ASCII.GetBytes(statementMarker);

        await using var update = new MySqlCommand(
            $"UPDATE /*{statementMarker}*/ {store.QualifiedTableName} SET `Value` = @value "
            + "WHERE `Id` = CAST('native-probe' AS BINARY);",
            worker);

        // WHY: An unprepared binary parameter puts invalid UTF-8 bytes in real native process metadata.
        // A synthetic SELECT of @statement cannot exercise MySQL's INFORMATION_SCHEMA row construction.
        update.Parameters.Add("@value", MySqlDbType.LongBlob)
            .Value = new byte[]
        {
            0x80,
            0,
            0xFF,
            0x88,
            0x99,
        };

        var pending = update.ExecuteNonQueryAsync(CancellationToken.None);
        Exception? primaryFailure = null;
        (long Id, byte[] Statement, string? State) nativeProcess;
        long observedMatches;
        bool remainedBlocked;

        try
        {
            nativeProcess = await CaptureNativeProbeCommandAsync(
                    observer,
                    worker.ServerThread,
                    markerBytes,
                    tableLock,
                    pending,
                    CancellationToken.None)
                .ConfigureAwait(false);

            await using var probe = observer.CreateCommand();

            // WHY: The native row was read above, including real source construction and binary conversion.
            // Re-reading live metadata here can lose INFO transiently; classify the exact captured row instead.
            const string processSource = "(SELECT @worker AS ID, @statement AS INFO, @state AS STATE) AS process";

            probe.CommandText = tableLock
                ? GetWaitingForTableLockProbeSql(processSource)
                : GetLockingStatementProbeSql(processSource);

            probe.Parameters.Add("@worker", MySqlDbType.Int64).Value = nativeProcess.Id;
            probe.Parameters.Add("@statement", MySqlDbType.LongBlob).Value = nativeProcess.Statement;
            probe.Parameters.Add("@state", MySqlDbType.VarChar).Value = (object?)nativeProcess.State ?? DBNull.Value;
            probe.Parameters.AddWithValue(
                "@tableName",
                matchingTable
                    ? store.TableName
                    : store.TableName + "_unrelated");

            if (!tableLock)
            {
                probe.Parameters.AddWithValue("@cleanupOnly", cleanupOnly);
            }

            // Act
            observedMatches = Convert.ToInt64(
                await probe
                    .ExecuteScalarAsync(CancellationToken.None)
                    .ConfigureAwait(false),
                CultureInfo.InvariantCulture);

            remainedBlocked = !pending.IsCompleted;
        }
        catch (Exception failure)
        {
            primaryFailure = failure;

            throw;
        }
        finally
        {
            // WHY: Unlocking and draining the owned UPDATE must finish even after the observer has failed.
            await CompleteLockProbeAsync(null, ReleaseLockAsync, pending, primaryFailure)
                .ConfigureAwait(false);
        }

        // Assert
        Assert.Equal((long)worker.ServerThread, nativeProcess.Id);
        Assert.NotEqual((long)observer.ServerThread, nativeProcess.Id);
        Assert.True(nativeProcess.Statement.AsSpan().IndexOf(markerBytes) >= 0);
        Assert.True(
            remainedBlocked,
            "The binary command must still be waiting while its native metadata is inspected.");

        Assert.Equal(expectedMatches, observedMatches);
        Assert.Equal(1, await pending.ConfigureAwait(false));

        // WHY: Releasing the lock is protected teardown, not abortable forward work.
        async Task ReleaseLockAsync()
        {
            if (transaction is { } rowLock)
            {
                await rowLock
                    .RollbackAsync(CancellationToken.None)
                    .ConfigureAwait(false);

                return;
            }

            await using var release = new MySqlCommand("UNLOCK TABLES;", blocker);
            await release
                .ExecuteNonQueryAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Captures native metadata for the current pending command without consulting the matcher.</summary>
    /// <param name="observer">The independent connection reading native session state.</param>
    /// <param name="workerId">The session executing the blocked binary UPDATE.</param>
    /// <param name="statementMarker">The unique ASCII marker identifying this command rather than a pooled predecessor.</param>
    /// <param name="tableLock">Whether to observe a metadata lock rather than an InnoDB record lock.</param>
    /// <param name="pending">The owned UPDATE, observed if it completes before reaching its lock.</param>
    /// <param name="cancellationToken">The token for the abortable observation.</param>
    private static async Task<(long Id, byte[] Statement, string? State)> CaptureNativeProbeCommandAsync(
        MySqlConnection observer,
        int workerId,
        byte[] statementMarker,
        bool tableLock,
        Task pending,
        CancellationToken cancellationToken
    )
    {
        // WHY: MariaDB's cached INNODB_TRX can still describe a previous command on a pooled session.
        // Observe this invocation's marker in the real native source while the independently held lock blocks it.
        var source = GetNativeProcessListSource(observer);
        var commandText = $"SELECT ID, CAST(INFO AS BINARY), STATE FROM {source} WHERE ID = @worker;";

        await using var command = new MySqlCommand(commandText, observer);

        command.Parameters.AddWithValue("@worker", workerId);

        while (!pending.IsCompleted)
        {
            await using (var reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    // WHY: These values belong to the already buffered row; reading them performs no I/O.
                    // ReSharper disable once MethodHasAsyncOverload
                    var statement = reader.IsDBNull(1) ? null : reader.GetFieldValue<byte[]>(1);

                    // WHY: Read STATE from the same buffered snapshot instead of another live metadata query.
                    // ReSharper disable once MethodHasAsyncOverload
                    var state = reader.IsDBNull(2) ? null : reader.GetString(2);

                    if (IsCurrentNativeProbeSnapshot(statement, statementMarker, tableLock, state))
                    {
                        return (reader.GetInt64(0), statement, state);
                    }
                }
            }

            // WHY: Yield while the current native statement becomes visible; elapsed time never establishes readiness.
            await Task
                .Delay(200, cancellationToken)
                .ConfigureAwait(false);
        }

        await pending.ConfigureAwait(false);

        throw new Xunit.Sdk.XunitException(
            "The binary UPDATE completed before the observer could inspect its native lock state.");
    }

    /// <summary>Recognizes current-command metadata without using the SQL classifier being tested.</summary>
    /// <param name="statement">The binary projection of the native INFO column, or absent metadata.</param>
    /// <param name="statementMarker">The independent nonempty marker belonging to this invocation.</param>
    /// <param name="tableLock">Whether native metadata must also report a table-lock wait.</param>
    /// <param name="state">The unmodified state from the same native row.</param>
    private static bool IsCurrentNativeProbeSnapshot(
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] byte[]? statement,
        ReadOnlySpan<byte> statementMarker,
        bool tableLock,
        string? state
    ) => statement is not null
        && statement.AsSpan().IndexOf(statementMarker) >= 0
        && (!tableLock || state?.Contains("lock", StringComparison.OrdinalIgnoreCase) == true);
}
