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
                if (!IntegrationTestEnvironment.GetSupportedTargets().Contains(targets[index]))
                {
                    continue;
                }

                var targetName = targets[index].ToString();
                cases.Add(targetName, false, true, false, 1L);
                cases.Add(targetName, false, false, false, 0L);
                cases.Add(targetName, false, true, true, 0L);
                cases.Add(targetName, true, true, false, 1L);
                cases.Add(targetName, true, false, false, 0L);
            }

            return cases;
        }
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

        await store.Cache
            .SetAsync("native-probe", [1], new DistributedCacheEntryOptions(), CancellationToken.None)
            .ConfigureAwait(false);

        await using var blocker = new MySqlConnection(store.ConnectionString);
        await using var worker = new MySqlConnection(store.ConnectionString);
        await using var observer = new MySqlConnection(store.ConnectionString);

        await blocker.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await worker.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await observer.OpenAsync(CancellationToken.None).ConfigureAwait(false);

        await using var transaction = tableLock
            ? null
            : await blocker.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false);

        await using (var acquire = new MySqlCommand(
            tableLock
                ? $"LOCK TABLES {store.QualifiedTableName} WRITE;"
                : $"SELECT `Revision` FROM {store.QualifiedTableName} "
                  + "WHERE `Id` = CAST('native-probe' AS BINARY) FOR UPDATE;",
            blocker,
            transaction))
        {
            await acquire.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);
        }

        await using var update = new MySqlCommand(
            $"UPDATE {store.QualifiedTableName} SET `Value` = @value "
            + "WHERE `Id` = CAST('native-probe' AS BINARY);",
            worker);

        // WHY: An unprepared binary parameter puts invalid UTF-8 bytes in real native process metadata.
        // A synthetic SELECT of @statement cannot exercise MySQL's INFORMATION_SCHEMA row construction.
        update.Parameters.Add("@value", MySqlDbType.LongBlob).Value = new byte[] { 0x80, 0, 0xFF, 0x88, 0x99 };
        var pending = update.ExecuteNonQueryAsync(CancellationToken.None);
        Exception? primaryFailure = null;
        long observedMatches;
        bool remainedBlocked;

        try
        {
            await WaitForNativeProbeCommandAsync(
                    observer,
                    target,
                    worker.ServerThread,
                    tableLock,
                    pending,
                    CancellationToken.None)
                .ConfigureAwait(false);

            await using var probe = observer.CreateCommand();
            var processSource = GetNativeProcessListSource(observer);
            probe.CommandText = tableLock
                ? GetWaitingForTableLockProbeSql(processSource)
                : GetLockingStatementProbeSql(processSource);

            probe.Parameters.AddWithValue(
                "@tableName",
                matchingTable ? store.TableName : store.TableName + "_unrelated");

            if (!tableLock)
            {
                probe.Parameters.AddWithValue("@cleanupOnly", cleanupOnly);
            }

            // Act
            observedMatches = Convert.ToInt64(
                await probe.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false),
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
            await CompleteLockProbeAsync(null, ReleaseLockAsync, pending, primaryFailure).ConfigureAwait(false);
        }

        // Assert
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
                await rowLock.RollbackAsync(CancellationToken.None).ConfigureAwait(false);

                return;
            }

            await using var release = new MySqlCommand("UNLOCK TABLES;", blocker);
            await release.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>Waits for the exact worker session to reach its native database lock state.</summary>
    /// <param name="observer">The independent connection reading native session state.</param>
    /// <param name="target">The engine whose instrumentation is available.</param>
    /// <param name="workerId">The session executing the blocked binary UPDATE.</param>
    /// <param name="tableLock">Whether to observe a metadata lock rather than an InnoDB record lock.</param>
    /// <param name="pending">The owned UPDATE, observed if it completes before reaching its lock.</param>
    /// <param name="cancellationToken">The token for the abortable observation.</param>
    private static async Task WaitForNativeProbeCommandAsync(
        MySqlConnection observer,
        IntegrationDatabaseTarget target,
        int workerId,
        bool tableLock,
        Task pending,
        CancellationToken cancellationToken
    )
    {
        // WHY: Arrange from the exact native session state, independently of the command matcher under test.
        // The held lock prevents completion; a command failure is observed instead of being hidden by polling.
        var isMySql = target is IntegrationDatabaseTarget.MySql84 or IntegrationDatabaseTarget.MySql97;
        var source = isMySql
            ? "performance_schema.processlist"
            : "information_schema.PROCESSLIST";

        var commandText = (isMySql, tableLock) switch
        {
            (true, false) => """
                             SELECT COUNT(*) FROM performance_schema.data_lock_waits AS waits
                             JOIN performance_schema.threads AS threads
                                 ON threads.THREAD_ID = waits.REQUESTING_THREAD_ID
                             WHERE threads.PROCESSLIST_ID = @worker;
                             """,
            (false, false) => """
                              SELECT COUNT(*) FROM information_schema.INNODB_TRX
                              WHERE TRX_MYSQL_THREAD_ID = @worker AND TRX_STATE = 'LOCK WAIT';
                              """,
            _ => $"""
                  SELECT COUNT(*) FROM {source}
                  WHERE ID = @worker AND LOWER(COALESCE(STATE, '')) LIKE '%lock%';
                  """,
        };

        await using var command = new MySqlCommand(commandText, observer);

        command.Parameters.AddWithValue("@worker", workerId);

        while (!pending.IsCompleted)
        {
            var visible = Convert.ToInt64(
                await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture);

            if (visible > 0)
            {
                return;
            }

            // WHY: InnoDB's transaction snapshot needs an interval without reads before it refreshes.
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        await pending.ConfigureAwait(false);
        Assert.Fail("The binary UPDATE completed before the observer could inspect its native lock state.");
    }
}
