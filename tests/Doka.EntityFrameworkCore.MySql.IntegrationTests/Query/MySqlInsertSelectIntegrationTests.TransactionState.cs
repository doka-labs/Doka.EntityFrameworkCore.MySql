namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlInsertSelectIntegrationTests
{
    private static readonly string[] s_stateOriginalRows = ["1|10|first", "2|20|second"];

    /// <summary>Reports direct and compiled execution against every selected normal profile.</summary>
    public static TheoryData<string, bool> StaleTransactionCases
    {
        get
        {
            var data = new TheoryData<string, bool>();

            foreach (var target in SelectedTargets)
            {
                data.Add(target, false);
                data.Add(target, true);
            }

            return data;
        }
    }

    /// <summary>Reports startup-only full-timeout rollback on the two pinned family representatives.</summary>
    public static TheoryData<string, bool> TimeoutOnCases
    {
        get
        {
            var data = new TheoryData<string, bool>();

            foreach (var target in SelectedTargets)
            {
                if (target is "MySql84" or "MariaDb123")
                {
                    data.Add(target, false);
                    data.Add(target, true);
                }
            }

            return data;
        }
    }

    /// <summary>A native deadlock outside the API cannot turn a subsequent direct or compiled insert into autocommit.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(StaleTransactionCases))]
    public async Task Externally_aborted_transaction_rejects_direct_and_compiled_insert_before_data_write(
        string target,
        bool compiled
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        await SeedStateRowsAsync(store, safety.Token);
        var commands = new InsertCommands();
        await using var context = store.CreateContext(commands);
        await using var transaction = await context.Database.BeginTransactionAsync(safety.Token);
        var worker = (MySqlConnection)context.Database.GetDbConnection();
        var nativeTransaction = (MySqlTransaction)transaction.GetDbTransaction();
        await ExecuteNativeAsync(worker,
            $"INSERT INTO `{TargetTable}` (`Id`,`Value`,`order`) VALUES (5000,5000,'prior');",
            safety.Token,
            nativeTransaction);

        var nativeFailure = await MakeExternalDeadlockVictimAsync(store, worker, nativeTransaction, safety.Token);
        var associated = ReferenceEquals(nativeTransaction.Connection, worker);
        var priorCount = await CountPriorWorkAsync(worker, nativeTransaction, safety.Token);
        var operation = compiled
            ? CompileStateInsert()
            : null;

        // Act
        var error = await Record.ExceptionAsync(() => operation is null
            ? InsertStateRowAsync(context, safety.Token)
            : operation(context, safety.Token));

        var actual = await ReadNativeAsync(store.Observer,
            $"SELECT `Id`,`Value`,`order` FROM `{TargetTable}` ORDER BY `Id`;",
            safety.Token);

        // Assert
        Assert.Equal(1213, nativeFailure.Number);
        Assert.True(associated);
        Assert.Equal(0, priorCount);
        AssertNativeMissingSavepoint(error);
        Assert.Empty(commands.Statements);
        Assert.Equal(0, commands.Readers);
        Assert.Equal(s_stateOriginalRows, actual);
        Assert.Same(transaction, context.Database.CurrentTransaction);
        Assert.Same(worker, nativeTransaction.Connection);
        Assert.Equal(ConnectionState.Open, worker.State);
        _output.WriteLine(
            $"External deadlock receipt rejected INSERT DATA: {store.Version}; compiled={compiled}; visible controls={commands.TransactionControls.Count}.");
    }

    /// <summary>Full rollback on a native timeout is qualified on private startup-configured representatives without changing shared profiles.</summary>
    [RequiresDatabaseTargetsTheory(IntegrationDatabaseTarget.MySql84, IntegrationDatabaseTarget.MariaDb123)]
    [MemberData(nameof(TimeoutOnCases))]
    public async Task Timeout_on_full_rollback_rejects_a_later_direct_or_compiled_insert_before_data_write(
        string target,
        bool compiled
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var policy =
            await MySqlRollbackOnTimeoutFixture.StartAsync(Enum.Parse<IntegrationDatabaseTarget>(target), safety.Token);

        await using var store = await InsertStore.CreateAsync(target, safety.Token, policy.ConnectionString);
        await SeedStateRowsAsync(store, safety.Token);
        await using var holder = await store.OpenNativeAsync(safety.Token);
        await using var heldTransaction = await holder.BeginTransactionAsync(safety.Token);
        await ExecuteNativeAsync(holder,
            $"UPDATE `{TargetTable}` SET `Value`=99 WHERE `Id`=2;",
            safety.Token,
            heldTransaction);

        var commands = new InsertCommands();
        await using var context = store.CreateContext(commands);
        await using var transaction = await context.Database.BeginTransactionAsync(safety.Token);
        var worker = (MySqlConnection)context.Database.GetDbConnection();
        var nativeTransaction = (MySqlTransaction)transaction.GetDbTransaction();
        await ExecuteNativeAsync(worker,
            $"INSERT INTO `{TargetTable}` (`Id`,`Value`,`order`) VALUES (5000,5000,'prior');",
            safety.Token,
            nativeTransaction);

        // WHY: Native 1205, not an elapsed-duration assertion, exercises the server's configured timeout rollback policy.
        await ExecuteNativeAsync(worker, "SET SESSION innodb_lock_wait_timeout=1;", safety.Token, nativeTransaction);
        var nativeFailure = await Record.ExceptionAsync(() => ExecuteNativeAsync(worker,
            $"UPDATE `{TargetTable}` SET `Value`=123 WHERE `Id`=2;",
            safety.Token,
            nativeTransaction));

        var priorCount = await CountPriorWorkAsync(worker, nativeTransaction, safety.Token);
        var operation = compiled
            ? CompileStateInsert()
            : null;

        // Act
        var error = await Record.ExceptionAsync(() => operation is null
            ? InsertStateRowAsync(context, safety.Token)
            : operation(context, safety.Token));

        await heldTransaction.RollbackAsync(safety.Token);
        var actual = await ReadNativeAsync(store.Observer,
            $"SELECT `Id`,`Value`,`order` FROM `{TargetTable}` ORDER BY `Id`;",
            safety.Token);

        // Assert
        Assert.Equal(1205,
            Assert.IsType<MySqlException>(nativeFailure)
                .Number);

        Assert.Equal(0, priorCount);
        AssertNativeMissingSavepoint(error);
        Assert.Empty(commands.Statements);
        Assert.Equal(0, commands.Readers);
        Assert.Equal(s_stateOriginalRows, actual);
        Assert.Same(transaction, context.Database.CurrentTransaction);
        Assert.Same(worker, nativeTransaction.Connection);
        Assert.Equal(ConnectionState.Open, worker.State);
        _output.WriteLine(
            $"Owned timeout-ON container={policy.ContainerId}; version={policy.Version}; compiled={compiled}; visible controls={commands.TransactionControls.Count}.");
    }

    /// <summary>A statement-only timeout retains prior work and a successful insertion inside the caller's explicit rollback.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Timeout_off_preserves_prior_work_and_the_subsequent_insert_until_caller_rollback(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        var flag = await ReadNativeAsync(store.Observer, "SELECT @@innodb_rollback_on_timeout;", safety.Token);

        if (flag is not ["0"])
        {
            throw new InvalidOperationException(
                "The ordinary pinned profile must retain its startup timeout-OFF policy.");
        }

        await SeedStateRowsAsync(store, safety.Token);
        await using var holder = await store.OpenNativeAsync(safety.Token);
        await using var heldTransaction = await holder.BeginTransactionAsync(safety.Token);
        await ExecuteNativeAsync(holder,
            $"UPDATE `{TargetTable}` SET `Value`=99 WHERE `Id`=2;",
            safety.Token,
            heldTransaction);

        var commands = new InsertCommands();
        await using var context = store.CreateContext(commands);
        await using var transaction = await context.Database.BeginTransactionAsync(safety.Token);
        var worker = (MySqlConnection)context.Database.GetDbConnection();
        var nativeTransaction = (MySqlTransaction)transaction.GetDbTransaction();
        await ExecuteNativeAsync(worker,
            $"INSERT INTO `{TargetTable}` (`Id`,`Value`,`order`) VALUES (5000,5000,'prior');",
            safety.Token,
            nativeTransaction);

        await ExecuteNativeAsync(worker, "SET SESSION innodb_lock_wait_timeout=1;", safety.Token, nativeTransaction);
        var nativeFailure = await Record.ExceptionAsync(() => ExecuteNativeAsync(worker,
            $"UPDATE `{TargetTable}` SET `Value`=123 WHERE `Id`=2;",
            safety.Token,
            nativeTransaction));

        // Act
        var affected = await InsertStateRowAsync(context, safety.Token);
        var priorCount = await CountPriorWorkAsync(worker, nativeTransaction, safety.Token);
        var readersBeforeExplicitReadback = commands.Readers;
        var insertedWithinTransaction = await context
            .Set<InsertTarget>()
            .CountAsync(row => row.Code == "alpha", safety.Token);

        var dataReaders = commands.Readers;
        await transaction.RollbackAsync(safety.Token);
        await heldTransaction.RollbackAsync(safety.Token);
        var actual = await ReadNativeAsync(store.Observer,
            $"SELECT `Id`,`Value`,`order` FROM `{TargetTable}` ORDER BY `Id`;",
            safety.Token);

        // Assert
        Assert.Equal(1205,
            Assert.IsType<MySqlException>(nativeFailure)
                .Number);

        Assert.Equal(1, affected);
        Assert.Equal(1, priorCount);
        Assert.Equal(1, insertedWithinTransaction);
        Assert.Equal(0, readersBeforeExplicitReadback);
        Assert.Equal(1, dataReaders);
        Assert.Single(commands.Statements);
        Assert.Equal(s_stateOriginalRows, actual);
        _output.WriteLine(
            $"Timeout-OFF retained caller work: {store.Version}; visible controls={commands.TransactionControls.Count}.");
    }

    /// <summary>Records a plain source-to-target insertion with the caller's required token.</summary>
    private static Task<int> InsertStateRowAsync(
        InsertContext context,
        CancellationToken cancellationToken
    ) => context
        .Set<InsertSource>()
        .Where(row => row.Id == 1)
        .ExecuteInsertFromQueryAsync(context.Set<InsertTarget>(),
            setters => setters
                .SetProperty(row => row.Value, row => row.Value)
                .SetProperty(row => row.Code, row => row.Label),
            cancellationToken);

    /// <summary>Builds a fresh compiled delegate per model, retaining runtime transaction validation on each invocation.</summary>
    private static Func<InsertContext, CancellationToken, Task<int>> CompileStateInsert()
        => EF.CompileQuery((InsertContext context, CancellationToken token) => context
            .Set<InsertSource>()
            .Where(row => row.Id == 1)
            .ExecuteInsertFromQueryAsync(context.Set<InsertTarget>(),
                setters => setters
                    .SetProperty(row => row.Value, row => row.Value)
                    .SetProperty(row => row.Code, row => row.Label),
                token));

    /// <summary>Creates known committed rows without involving this feature's execution path.</summary>
    private static Task<int> SeedStateRowsAsync(
        InsertStore store,
        CancellationToken cancellationToken
    ) => ExecuteNativeAsync(store.Observer,
        $"INSERT INTO `{TargetTable}` (`Id`,`Value`,`order`) VALUES (1,10,'first'),(2,20,'second');",
        cancellationToken);

    /// <summary>Reads caller work through the still-associated native transaction object rather than inferring state from that association.</summary>
    private static async Task<int> CountPriorWorkAsync(
        MySqlConnection worker,
        MySqlTransaction transaction,
        CancellationToken cancellationToken
    )
    {
        await using var command =
            new MySqlCommand($"SELECT COUNT(*) FROM `{TargetTable}` WHERE `Id`=5000;", worker, transaction);

        return Convert.ToInt32(await command
                .ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    /// <summary>Requires the engine's missing-marker proof; unrelated metadata or translation exceptions cannot satisfy the guard.</summary>
    private static void AssertNativeMissingSavepoint(
        Exception? exception
    )
    {
        Assert.NotNull(exception);
        var causes = exception is AggregateException aggregate
            ? aggregate
                .Flatten()
                .InnerExceptions
                .ToArray()
            : [exception];

        Assert.NotEmpty(causes);
        Assert.All(causes,
            cause => Assert.Equal(1305,
                Assert.IsType<MySqlException>(cause)
                    .Number));
    }

    /// <summary>Aborts the native caller transaction outside INSERT SELECT, leaving its misleading client association intact.</summary>
    private static async Task<MySqlException> MakeExternalDeadlockVictimAsync(
        InsertStore store,
        MySqlConnection worker,
        MySqlTransaction workerTransaction,
        CancellationToken cancellationToken
    )
    {
        using var probe = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await ExecuteNativeAsync(store.Observer,
            "CREATE TABLE InsertDeadlockWeight (`Id` INT PRIMARY KEY) ENGINE=InnoDB;",
            probe.Token);

        await using var holder = await store.OpenNativeAsync(probe.Token);
        await using var heldTransaction = await holder.BeginTransactionAsync(probe.Token);
        await ExecuteNativeAsync(holder,
            $"UPDATE `{TargetTable}` SET `Value`=99 WHERE `Id`=2;",
            probe.Token,
            heldTransaction);

        var weight = string.Join(',',
            Enumerable
                .Range(1, 2000)
                .Select(id => "(" + id.ToString(CultureInfo.InvariantCulture) + ")"));

        await ExecuteNativeAsync(holder,
            "INSERT INTO InsertDeadlockWeight VALUES " + weight + ";",
            probe.Token,
            heldTransaction);

        await ExecuteNativeAsync(worker,
            $"UPDATE `{TargetTable}` SET `Value`=88 WHERE `Id`=1;",
            probe.Token,
            workerTransaction);

        var pending = ExecuteNativeAsync(worker,
            $"UPDATE `{TargetTable}` SET `Value`=77 WHERE `Id`=2;",
            probe.Token,
            workerTransaction);

        Exception? primary = null;
        MySqlException? victim = null;

        try
        {
            await MySqlNativeLockWaitObserver.ObserveAsync(store.Version.IsMariaDb,
                store.Observer,
                worker,
                pending,
                probe.Token);

            await ExecuteNativeAsync(holder,
                $"UPDATE `{TargetTable}` SET `Value`=66 WHERE `Id`=1;",
                probe.Token,
                heldTransaction);

            victim = Assert.IsType<MySqlException>(await Record.ExceptionAsync(async () => _ = await pending));
            primary = victim;
        }
        catch (Exception failure)
        {
            primary = failure;
            throw;
        }
        finally
        {
            // WHY: Release only the owned holder and drain its counterpart; deliberately leave the server-aborted caller associated for the later guard.
            await DatabaseLockProbeCleanup.CompleteLockProbeAsync(probe,
                () => heldTransaction.RollbackAsync(CancellationToken.None),
                pending,
                primary,
                null);
        }

        return victim
            ?? throw new InvalidOperationException("The native cycle did not produce its intended transaction victim.");
    }
}
