using IsolationLevel = System.Transactions.IsolationLevel;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlLockingReadIntegrationTests
{
    /// <summary>Missing and completed local transactions both fail before a locking SELECT reaches the driver.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(BooleanCases))]
    public async Task Missing_or_completed_transaction_rejects_before_the_data_command(
        string target,
        bool completed
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await LockStore.CreateAsync(target, safety.Token);
        var commands = new LockCommands();
        await using var context = store.CreateContext(commands);

        if (completed)
        {
            await using var transaction = await context.Database.BeginTransactionAsync(safety.Token);
            await transaction.CommitAsync(safety.Token);
        }

        // Act
        var exception = await Record.ExceptionAsync(() => context
            .Set<LockRow>()
            .ForUpdate()
            .ToListAsync(safety.Token));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Empty(commands.Statements);
    }

    /// <summary>EF explicitly enlists an already open borrowed connection before the locking SELECT despite connector AutoEnlist being disabled.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Already_open_connection_is_explicitly_enlisted_by_ef_before_locking_select(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await LockStore.CreateAsync(target, safety.Token);
        await using var connection = await store.OpenNativeAsync(safety.Token);
        var commands = new LockCommands();
        await using var context = store.CreateContext(commands, connection: connection);
        int id;
        int held;

        // Act
        using (var scope =
            new TransactionScope(TransactionScopeOption.RequiresNew, TransactionScopeAsyncFlowOption.Enabled))
        {
            // WHY: AutoEnlist=false only disables connector-open enlistment. EF's command-open
            // path can explicitly enlist this already open connection; actual locks prove that binding.
            id = (await context
                .Set<LockRow>()
                .Where(row => row.Id == 1)
                .ForUpdate()
                .SingleAsync(safety.Token)).Id;

            held = await ProbePkNoWaitAsync(store, RowsTable, 1, safety.Token);
            scope.Complete();
        }

        var released = await ProbePkNoWaitAsync(store, RowsTable, 1, safety.Token);

        // Assert
        Assert.Equal(1, id);
        AssertNativeConflict(store, held);
        Assert.Equal(0, released);
        Assert.Same(connection, Assert.Single(commands.Connections));
    }

    /// <summary>An actually enlisted ambient connection holds the native row lock until successful scope completion.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Actually_enlisted_ambient_transaction_holds_and_releases_the_native_lock(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await LockStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        await using var writer = await store.OpenNativeAsync(safety.Token);
        await using var update =
            new MySqlCommand($"UPDATE `{RowsTable}` SET `Value` = `Value` + 1 WHERE `Id` = 1;", writer);

        var scope = new TransactionScope(TransactionScopeOption.RequiresNew,
            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled);

        var released = false;
        var pending = Task.FromResult(0);
        Exception? primary = null;
        int affected;
        int value;

        try
        {
            await context.Database.OpenConnectionAsync(safety.Token);
            _ = await context
                .Set<LockRow>()
                .Where(row => row.Id == 1)
                .ForUpdate()
                .SingleAsync(safety.Token);

            // Act
            pending = update.ExecuteNonQueryAsync(safety.Token);
            await WaitForNativeLockWaitAsync(store, writer, pending, safety.Token);
            scope.Complete();
            scope.Dispose();
            released = true;
            affected = await pending;
            value = await ReadValueAsync(store.Observer, safety.Token);
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
        Assert.Equal(1, affected);
        Assert.Equal(11, value);

        // WHY: An incomplete scope rolls back during cleanup; it must be disposed
        // before draining the writer even if forward work or observation failed.
        Task ReleaseAsync()
        {
            if (!released)
            {
                scope.Dispose();
                released = true;
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>Compiled delegates recheck each connection's current transaction instead of retaining the first execution's proof.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Compiled_query_revalidates_each_context_and_execution_transaction(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await LockStore.CreateAsync(target, safety.Token);
        var firstCommands = new LockCommands();
        var secondCommands = new LockCommands();
        await using var first = store.CreateContext(firstCommands);
        await using var second = store.CreateContext(secondCommands);
        var compiled = EF.CompileAsyncQuery((LockContext context, int id) => context
            .Set<LockRow>()
            .Where(row => row.Id == id)
            .ForUpdate());

        await using var firstTransaction = await first.Database.BeginTransactionAsync(safety.Token);

        // Act
        var firstRows = await MaterializeAsync(compiled(first, 1), safety.Token);
        await firstTransaction.CommitAsync(safety.Token);
        var missing = await Record.ExceptionAsync(() => MaterializeAsync(compiled(second, 1), safety.Token));
        await using var secondTransaction = await second.Database.BeginTransactionAsync(safety.Token);
        var secondRows = await MaterializeAsync(compiled(second, 1), safety.Token);
        var held = await ProbePkNoWaitAsync(store, RowsTable, 1, safety.Token);

        // Assert
        Assert.Equal(1,
            Assert.Single(firstRows)
                .Id);

        Assert.Equal(1,
            Assert.Single(secondRows)
                .Id);

        Assert.IsType<InvalidOperationException>(missing);
        Assert.Single(firstCommands.Statements);
        Assert.Single(secondCommands.Statements);
        Assert.NotSame(Assert.Single(firstCommands.Connections), Assert.Single(secondCommands.Connections));
        AssertNativeConflict(store, held);
    }

    /// <summary>A returned pooled context carries neither a previous transaction grant nor the previous query's locking mode.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Pool_reuse_preserves_ordinary_queries_and_rechecks_a_new_locking_execution(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await LockStore.CreateAsync(target, safety.Token);
        var commands = new LockCommands();
        await using var services = store.CreatePooledServices(commands);
        var factory = services.GetRequiredService<IDbContextFactory<LockContext>>();
        LockContext original;

        await using (var initial = await factory.CreateDbContextAsync(safety.Token))
        {
            original = initial;
            await using var transaction = await initial.Database.BeginTransactionAsync(safety.Token);
            _ = await initial
                .Set<LockRow>()
                .Where(row => row.Id == 1)
                .ForUpdate()
                .SingleAsync(safety.Token);

            await transaction.CommitAsync(safety.Token);
        }

        // Act
        await using var reused = await factory.CreateDbContextAsync(safety.Token);
        var ordinary = await reused
            .Set<LockRow>()
            .Where(row => row.Id == 1)
            .SingleAsync(safety.Token);

        var free = await ProbePkNoWaitAsync(store, RowsTable, 1, safety.Token);
        var missing = await Record.ExceptionAsync(() => reused
            .Set<LockRow>()
            .ForShare()
            .ToListAsync(safety.Token));

        await using var current = await reused.Database.BeginTransactionAsync(safety.Token);
        var shared = await reused
            .Set<LockRow>()
            .Where(row => row.Id == 1)
            .ForShare()
            .SingleAsync(safety.Token);

        var held = await ProbePkNoWaitAsync(store, RowsTable, 1, safety.Token);

        // Assert
        Assert.Same(original, reused);
        Assert.Equal(ordinary.Id, shared.Id);
        Assert.Equal(0, free);
        Assert.IsType<InvalidOperationException>(missing);
        Assert.Equal(3, commands.Statements.Count);
        Assert.DoesNotContain("FOR UPDATE", commands.Statements[1], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FOR SHARE", commands.Statements[1], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LOCK IN SHARE MODE", commands.Statements[1], StringComparison.OrdinalIgnoreCase);
        AssertNativeConflict(store, held);
    }

    /// <summary>Cancellation of a server-observed blocked SELECT is a real command cancellation followed by explicit rollback and reuse.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Blocked_locking_read_cancellation_drains_and_releases_owned_transactions(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(safety.Token);
        await using var store = await LockStore.CreateAsync(target, safety.Token);
        await using var holder = store.CreateContext();
        await using var waiting = store.CreateContext();
        await using var heldTransaction = await holder.Database.BeginTransactionAsync(safety.Token);
        await using var waitingTransaction = await waiting.Database.BeginTransactionAsync(safety.Token);
        _ = await holder
            .Set<LockRow>()
            .Where(row => row.Id == 1)
            .ForUpdate()
            .SingleAsync(safety.Token);

        var connection = (MySqlConnection)waiting.Database.GetDbConnection();
        Exception? primary = null;
        Exception? canceled = null;

        // Act
        var pending = waiting
            .Set<LockRow>()
            .Where(row => row.Id == 1)
            .ForUpdate()
            .ToListAsync(cancellation.Token);

        try
        {
            await WaitForNativeLockWaitAsync(store, connection, pending, safety.Token);
            await cancellation.CancelAsync();
            canceled = await Record.ExceptionAsync(async () => _ = await pending);
            primary = canceled;
        }
        catch (Exception failure)
        {
            primary = failure;
            throw;
        }
        finally
        {
            await DatabaseLockProbeCleanup.CompleteLockProbeAsync(cancellation, ReleaseAsync, pending, primary, null);
        }

        var afterRelease = await ProbePkNoWaitAsync(store, RowsTable, 1, safety.Token);

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(canceled);
        Assert.Equal(0, afterRelease);

        // WHY: Both participants require rollback independently of the canceled command token.
        async Task ReleaseAsync()
        {
            await RollbackOwnedAsync(heldTransaction);
            await RollbackOwnedAsync(waitingTransaction);
        }
    }

    /// <summary>Observer exceptions and cancellations retain their identity while every pending command is canceled, unlocked and observed.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(BooleanCases))]
    public async Task Observer_failure_keeps_primary_exception_and_drains_owned_work(
        string target,
        bool cancellationFailure
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(safety.Token);
        using var observerCancellation = CancellationTokenSource.CreateLinkedTokenSource(safety.Token);
        await using var store = await LockStore.CreateAsync(target, safety.Token);
        await using var holder = store.CreateContext();
        await using var transaction = await holder.Database.BeginTransactionAsync(safety.Token);
        _ = await holder
            .Set<LockRow>()
            .Where(row => row.Id == 1)
            .ForUpdate()
            .SingleAsync(safety.Token);

        await using var writer = await store.OpenNativeAsync(safety.Token);
        await using var command =
            new MySqlCommand($"UPDATE `{RowsTable}` SET `Value` = `Value` + 1 WHERE `Id` = 1;", writer);

        var pending = command.ExecuteNonQueryAsync(cancellation.Token);
        await WaitForNativeLockWaitAsync(store, writer, pending, safety.Token);
        var expected = new InvalidOperationException("observer failure");
        Exception? observed = null;

        // Act
        var actual = await Record.ExceptionAsync(async () =>
        {
            Exception? primary = null;

            try
            {
                if (cancellationFailure)
                {
                    await observerCancellation.CancelAsync();
                    await WaitForNativeLockWaitAsync(store, writer, pending, observerCancellation.Token);

                    throw new XunitException("The canceled observer unexpectedly completed.");
                }

                throw expected;
            }
            catch (Exception failure)
            {
                primary = failure;
                observed = failure;
                throw;
            }
            finally
            {
                await DatabaseLockProbeCleanup.CompleteLockProbeAsync(cancellation,
                    () => RollbackOwnedAsync(transaction),
                    pending,
                    primary,
                    null);
            }
        });

        var reusable = await ProbePkNoWaitAsync(store, RowsTable, 1, safety.Token);

        // Assert
        Assert.Same(observed, actual);

        if (cancellationFailure)
        {
            Assert.Equal(observerCancellation.Token,
                Assert.IsAssignableFrom<OperationCanceledException>(actual)
                    .CancellationToken);
        }
        else
        {
            Assert.Same(expected, actual);
        }

        Assert.True(pending.IsCompleted);
        Assert.Equal(0, reusable);
    }
}
