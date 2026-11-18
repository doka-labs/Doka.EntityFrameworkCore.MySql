namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlLockingReadIntegrationTests
{
    /// <summary>Reports an independent boolean variant for every selected supported engine.</summary>
    public static IEnumerable<TheoryDataRow<string, bool>> BooleanCases => SelectedTargets.SelectMany(target => new[]
    {
        new TheoryDataRow<string, bool>(target.Data, false), new TheoryDataRow<string, bool>(target.Data, true),
    });

    /// <summary>An actual waiting writer continues after caller commit and observes the committed holder modification.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Blocked_writer_continues_after_explicit_commit(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await LockStore.CreateAsync(target, safety.Token);
        await using var holder = store.CreateContext();
        await using var transaction = await holder.Database.BeginTransactionAsync(safety.Token);
        await holder.Database.ExecuteSqlRawAsync($"UPDATE `{RowsTable}` SET `Value` = 100 WHERE `Id` = 1;",
            safety.Token);

        _ = await holder
            .Set<LockRow>()
            .Where(row => row.Id == 1)
            .ForUpdate()
            .SingleAsync(safety.Token);

        // Act
        var result = await CompleteBlockedWriterAsync(store, transaction, true, safety.Token);

        // Assert
        Assert.Equal(1, result.Affected);
        Assert.Equal(101, result.Value);
    }

    /// <summary>An actual waiting writer continues after caller rollback and cannot observe the discarded holder modification.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Blocked_writer_continues_after_explicit_rollback(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await LockStore.CreateAsync(target, safety.Token);
        await using var holder = store.CreateContext();
        await using var transaction = await holder.Database.BeginTransactionAsync(safety.Token);
        await holder.Database.ExecuteSqlRawAsync($"UPDATE `{RowsTable}` SET `Value` = 100 WHERE `Id` = 1;",
            safety.Token);

        _ = await holder
            .Set<LockRow>()
            .Where(row => row.Id == 1)
            .ForUpdate()
            .SingleAsync(safety.Token);

        // Act
        var result = await CompleteBlockedWriterAsync(store, transaction, false, safety.Token);

        // Assert
        Assert.Equal(1, result.Affected);
        Assert.Equal(11, result.Value);
    }

    /// <summary>Two shared readers coexist while their PK locks prevent a writer until both owners release them.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Shared_readers_coexist_and_block_the_pk_writer_until_both_release(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await LockStore.CreateAsync(target, safety.Token);
        await using var first = store.CreateContext();
        await using var second = store.CreateContext();
        await using var firstTransaction = await first.Database.BeginTransactionAsync(safety.Token);
        await using var secondTransaction = await second.Database.BeginTransactionAsync(safety.Token);
        var firstRow = await first
            .Set<LockRow>()
            .Where(row => row.Id == 1)
            .ForShare()
            .SingleAsync(safety.Token);

        var secondRow = await second
            .Set<LockRow>()
            .Where(row => row.Id == 1)
            .ForShare()
            .SingleAsync(safety.Token);

        await using var writer = await store.OpenNativeAsync(safety.Token);
        await using var update =
            new MySqlCommand($"UPDATE `{RowsTable}` SET `Value` = `Value` + 1 WHERE `Id` = 1;", writer);

        // Act
        var pending = update.ExecuteNonQueryAsync(safety.Token);
        Exception? primary = null;
        int afterFirstRelease;
        int affected;

        try
        {
            await WaitForNativeLockWaitAsync(store, writer, pending, safety.Token);
            await firstTransaction.RollbackAsync(safety.Token);
            afterFirstRelease = await ProbePkNoWaitAsync(store, RowsTable, 1, safety.Token);
            await secondTransaction.RollbackAsync(safety.Token);
            affected = await pending;
        }
        catch (Exception failure)
        {
            primary = failure;
            throw;
        }
        finally
        {
            await DatabaseLockProbeCleanup.CompleteLockProbeAsync(null, ReleaseAsync, pending, primary, null);
        }

        // Assert
        Assert.Equal(firstRow.Id, secondRow.Id);
        AssertNativeConflict(store, afterFirstRelease);
        Assert.Equal(1, affected);

        // WHY: Both owned transactions must be released even after forward cancellation.
        async Task ReleaseAsync()
        {
            await RollbackOwnedAsync(firstTransaction);
            await RollbackOwnedAsync(secondTransaction);
        }
    }

    /// <summary>NOWAIT reports the native conflict once, including when an outer execution strategy enables ordinary retries.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(BooleanCases))]
    public async Task Nowait_conflict_is_native_and_never_automatically_retried(
        string target,
        bool retries
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await LockStore.CreateAsync(target, safety.Token);
        await using var holder = store.CreateContext();
        await using var transaction = await holder.Database.BeginTransactionAsync(safety.Token);
        _ = await holder
            .Set<LockRow>()
            .Where(row => row.Id == 1)
            .ForUpdate()
            .SingleAsync(safety.Token);

        var commands = new LockCommands();
        await using var contenderServices = new ServiceCollection()
            .AddEntityFrameworkDokaMySql()
            .BuildServiceProvider();

        // WHY: Retry settings are an immutable provider contract. The contender intentionally
        // uses different settings from the holder, so it owns a separate service graph.
        var options = new DbContextOptionsBuilder<LockContext>(store.CreateOptions(commands, retries))
            .UseInternalServiceProvider(contenderServices)
            .Options;

        await using var contender = new LockContext(options);
        var strategy = contender.Database.CreateExecutionStrategy();

        // Act
        var exception = await Record.ExceptionAsync(() => strategy.ExecuteAsync(async () =>
        {
            // WHY: Retrying EF strategies legitimately reject externally initiated local transactions;
            // create this transaction inside their operation so the native NOWAIT path is exercised.
            await using var attempt = await contender.Database.BeginTransactionAsync(safety.Token);
            _ = await contender
                .Set<LockRow>()
                .Where(row => row.Id == 1)
                .ForUpdateNoWait()
                .SingleAsync(safety.Token);
        }));

        var stillHeld = await ProbePkNoWaitAsync(store, RowsTable, 1, safety.Token);

        // Assert
        AssertNativeConflict(store,
            Assert.IsType<MySqlException>(exception)
                .Number);

        Assert.Single(commands.Statements);
        AssertNativeConflict(store, stillHeld);
    }

    /// <summary>SKIP LOCKED returns exactly the available jobs, including the valid all-locked empty result.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(BooleanCases))]
    public async Task Skip_locked_returns_exactly_the_free_candidates_without_releasing_held_rows(
        string target,
        bool allLocked
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await LockStore.CreateAsync(target, safety.Token);
        await using var holder = store.CreateContext();
        await using var contender = store.CreateContext();
        await using var heldTransaction = await holder.Database.BeginTransactionAsync(safety.Token);
        await using var contenderTransaction = await contender.Database.BeginTransactionAsync(safety.Token);
        _ = await holder
            .Set<LockRow>()
            .Where(row => allLocked || row.Id == 1)
            .ForUpdate()
            .ToArrayAsync(safety.Token);

        // Act
        var free = await contender
            .Set<LockRow>()
            .OrderBy(row => row.Id)
            .ForUpdateSkipLocked()
            .ToArrayAsync(safety.Token);

        var stillHeld = await ProbePkNoWaitAsync(store, RowsTable, 1, safety.Token);

        // Assert
        Assert.Equal(allLocked
                ? 0
                : 2,
            free.Length);

        if (!allLocked)
        {
            Assert.Equal([2, 3], free.Select(row => row.Id));
        }

        AssertNativeConflict(store, stillHeld);
    }

    /// <summary>Both full reader consumption and early enumerator disposal retain the caller's lock until commit.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(BooleanCases))]
    public async Task Reader_end_and_early_dispose_do_not_commit_the_transaction(
        string target,
        bool earlyDispose
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await LockStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync(safety.Token);
        var query = context
            .Set<LockRow>()
            .OrderBy(row => row.Id)
            .ForUpdate()
            .AsAsyncEnumerable();

        var ids = new List<int>();

        // Act
        await using (var reader = query.GetAsyncEnumerator(safety.Token))
        {
            if (await reader.MoveNextAsync())
            {
                ids.Add(reader.Current.Id);
            }

            if (!earlyDispose)
            {
                while (await reader.MoveNextAsync())
                {
                    ids.Add(reader.Current.Id);
                }
            }
        }

        var held = await ProbePkNoWaitAsync(store, RowsTable, 1, safety.Token);
        await transaction.CommitAsync(safety.Token);
        var released = await ProbePkNoWaitAsync(store, RowsTable, 1, safety.Token);

        // Assert
        Assert.Equal(earlyDispose
                ? 1
                : 3,
            ids.Count);

        AssertNativeConflict(store, held);
        Assert.Equal(0, released);
    }

    /// <summary>Observes a genuine wait, releases the chosen owner state and drains the writer before returning.</summary>
    private static async Task<(int Affected, int Value)> CompleteBlockedWriterAsync(
        LockStore store,
        IDbContextTransaction transaction,
        bool commit,
        CancellationToken cancellationToken
    )
    {
        await using var writer = await store
            .OpenNativeAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var command =
            new MySqlCommand($"UPDATE `{RowsTable}` SET `Value` = `Value` + 1 WHERE `Id` = 1;", writer);

        var pending = command.ExecuteNonQueryAsync(cancellationToken);
        Exception? primary = null;

        try
        {
            await WaitForNativeLockWaitAsync(store, writer, pending, cancellationToken)
                .ConfigureAwait(false);

            if (commit)
            {
                await transaction
                    .CommitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await transaction
                    .RollbackAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            var affected = await pending.ConfigureAwait(false);
            var value = await ReadValueAsync(store.Observer, cancellationToken)
                .ConfigureAwait(false);

            return (affected, value);
        }
        catch (Exception failure)
        {
            primary = failure;
            throw;
        }
        finally
        {
            await DatabaseLockProbeCleanup
                .CompleteLockProbeAsync(null,
                    () => RollbackOwnedAsync(transaction),
                    pending,
                    primary,
                    null)
                .ConfigureAwait(false);
        }
    }
}
