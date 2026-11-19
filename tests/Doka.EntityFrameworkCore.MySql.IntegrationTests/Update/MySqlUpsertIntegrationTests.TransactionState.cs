namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlUpsertIntegrationTests
{
    /// <summary>Reports the two pinned representatives that qualify startup-only timeout rollback policy.</summary>
    public static TheoryData<string> TimeoutPolicyTargets
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var target in SelectedTargets)
            {
                if (Enum.Parse<IntegrationDatabaseTarget>(target) is IntegrationDatabaseTarget.MySql84
                    or IntegrationDatabaseTarget.MariaDb123)
                {
                    data.Add(target);
                }
            }

            return data;
        }
    }

    /// <summary>An engine-wide external timeout cannot turn a subsequent direct call into an autocommitted write.</summary>
    [RequiresDatabaseTargetsTheory(IntegrationDatabaseTarget.MySql84, IntegrationDatabaseTarget.MariaDb123)]
    [MemberData(nameof(TimeoutPolicyTargets))]
    public async Task External_full_rollback_timeout_is_rejected_before_upsert_data_commands(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var policy =
            await MySqlRollbackOnTimeoutFixture.StartAsync(Enum.Parse<IntegrationDatabaseTarget>(target), safety.Token);

        await using var store =
            await UpsertStore.CreateAsync(target, "ordinary", safety.Token, policy.ConnectionString);

        await using (var seed = store.CreateContext())
        {
            seed.AddRange(NewRow(1), NewRow(2));
            await seed.SaveChangesAsync(safety.Token);
        }

        await using var holder = new MySqlConnection(store.ConnectionString);
        await holder.OpenAsync(safety.Token);
        await using var holderTransaction = await holder.BeginTransactionAsync(safety.Token);
        await ExecuteNativeAsync(holder,
            "UPDATE `UpsertRows` SET `Unchanged`='held' WHERE `Id`=2;",
            safety.Token,
            holderTransaction);

        var capture = new Writes();
        await using var context = store.CreateContext(capture);
        await context.Database.OpenConnectionAsync(safety.Token);
        await using var transaction = await context.Database.BeginTransactionAsync(safety.Token);
        context.Add(NewRow(5000, "prior-work"));
        await context.SaveChangesAsync(safety.Token);
        var worker = (MySqlConnection)context.Database.GetDbConnection();
        var nativeTransaction = (MySqlTransaction)transaction.GetDbTransaction();
        await ExecuteNativeAsync(worker, "SET SESSION innodb_lock_wait_timeout=1;", safety.Token, nativeTransaction);
        var timeout = await Record.ExceptionAsync(() => ExecuteNativeAsync(worker,
            "UPDATE `UpsertRows` SET `Unchanged`='blocked' WHERE `Id`=2;",
            safety.Token,
            nativeTransaction));

        // Act
        var error = await Record.ExceptionAsync(() => context.ExecuteUpsertAsync(NewRow(6000, "must-not-autocommit"),
            new[] { nameof(UpsertRow.Name) },
            safety.Token));

        await holderTransaction.RollbackAsync(safety.Token);
        await using var reader = store.CreateContext();
        var survivingIds = await reader
            .Rows
            .AsNoTracking()
            .OrderBy(row => row.Id)
            .Select(row => row.Id)
            .ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal(1205,
            Assert.IsType<MySqlException>(timeout)
                .Number);

        Assert.NotNull(error);
        IEnumerable<Exception> causes = error is AggregateException aggregate
            ? aggregate.Flatten()
                .InnerExceptions
            : new[] { error };

        Assert.Contains(causes, cause => cause is MySqlException { Number: 1305 });
        Assert.Empty(capture.Statements);
        Assert.Equal(s_originalIds, survivingIds);
        Assert.Same(transaction, context.Database.CurrentTransaction);
        Assert.Same(worker, nativeTransaction.Connection);
        Assert.Equal(ConnectionState.Open, worker.State);
    }

    /// <summary>A statement-only native timeout keeps prior caller work and a subsequent UPSERT inside the caller rollback.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task External_statement_timeout_preserves_the_live_caller_transaction_and_its_prior_work(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await UpsertStore.CreateAsync(target, "ordinary", safety.Token);
        await using (var flag = new MySqlCommand("SELECT @@innodb_rollback_on_timeout;", store.Observer))
        {
            Assert.Equal(0, Convert.ToInt32(await flag.ExecuteScalarAsync(safety.Token), CultureInfo.InvariantCulture));
        }

        await using (var seed = store.CreateContext())
        {
            seed.AddRange(NewRow(1), NewRow(2));
            await seed.SaveChangesAsync(safety.Token);
        }

        await using var holder = new MySqlConnection(store.ConnectionString);
        await holder.OpenAsync(safety.Token);
        await using var holderTransaction = await holder.BeginTransactionAsync(safety.Token);
        await ExecuteNativeAsync(holder,
            "UPDATE `UpsertRows` SET `Unchanged`='held' WHERE `Id`=2;",
            safety.Token,
            holderTransaction);

        var capture = new Writes();
        await using var context = store.CreateContext(capture);
        await using var transaction = await context.Database.BeginTransactionAsync(safety.Token);
        context.Add(NewRow(5000, "prior-work"));
        await context.SaveChangesAsync(safety.Token);
        var worker = (MySqlConnection)context.Database.GetDbConnection();
        var nativeTransaction = (MySqlTransaction)transaction.GetDbTransaction();
        await ExecuteNativeAsync(worker, "SET SESSION innodb_lock_wait_timeout=1;", safety.Token, nativeTransaction);
        var timeout = await Record.ExceptionAsync(() => ExecuteNativeAsync(worker,
            "UPDATE `UpsertRows` SET `Unchanged`='blocked' WHERE `Id`=2;",
            safety.Token,
            nativeTransaction));

        // Act
        var affected = await context.ExecuteUpsertAsync(NewRow(6000, "caller-owned"),
            new[] { nameof(UpsertRow.Name) },
            safety.Token);

        var priorStillPresent = await context.Rows.AnyAsync(row => row.Id == 5000, safety.Token);
        await transaction.RollbackAsync(safety.Token);
        await holderTransaction.RollbackAsync(safety.Token);
        await using var reader = store.CreateContext();
        var survivingIds = await reader
            .Rows
            .AsNoTracking()
            .OrderBy(row => row.Id)
            .Select(row => row.Id)
            .ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal(1205,
            Assert.IsType<MySqlException>(timeout)
                .Number);

        Assert.Equal(1, affected);
        Assert.True(priorStillPresent);
        Assert.Single(capture.Statements);
        Assert.Equal(s_originalIds, survivingIds);
    }

    /// <summary>An externally aborted native transaction is rejected before a later UPSERT can autocommit.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task External_deadlock_victim_cannot_reuse_a_stale_transaction_for_an_autocommitted_upsert(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await UpsertStore.CreateAsync(target, "ordinary", safety.Token);
        await using (var seed = store.CreateContext())
        {
            seed.AddRange(NewRow(1, "first"), NewRow(2, "second"));
            await seed.SaveChangesAsync(safety.Token);
        }

        var capture = new Writes();
        await using var context = store.CreateContext(capture);
        await context.Database.OpenConnectionAsync(safety.Token);
        await using var transaction = await context.Database.BeginTransactionAsync(safety.Token);
        context.Add(NewRow(5000, "prior-work"));
        await context.SaveChangesAsync(safety.Token);
        var nativeError = await MakeCallerTheDeadlockVictimAsync(store, context, safety.Token);
        var connection = (MySqlConnection)context.Database.GetDbConnection();
        var associated = ReferenceEquals(transaction.GetDbTransaction()
                .Connection,
            connection);

        var writesBeforeCall = capture.Statements.Count;

        // Act
        var error = await Record.ExceptionAsync(() => context.ExecuteUpsertAsync(NewRow(6000, "must-not-autocommit"),
            new[] { nameof(UpsertRow.Name) },
            safety.Token));

        await using var reader = store.CreateContext();
        var survivingIds = await reader
            .Rows
            .AsNoTracking()
            .OrderBy(row => row.Id)
            .Select(row => row.Id)
            .ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal(1213,
            Assert.IsType<MySqlException>(nativeError)
                .Number);

        Assert.True(associated);
        Assert.NotNull(error);
        IEnumerable<Exception> causes = error is AggregateException aggregate
            ? aggregate.Flatten()
                .InnerExceptions
            : new[] { error };

        Assert.Contains(causes, cause => cause is MySqlException { Number: 1305 });
        Assert.Equal(writesBeforeCall, capture.Statements.Count);
        Assert.Equal(s_originalIds, survivingIds);
        Assert.Same(transaction, context.Database.CurrentTransaction);
        Assert.Equal(ConnectionState.Open, connection.State);
    }

    /// <summary>Creates a real native deadlock outside the feature API, using undo weight and observed lock readiness.</summary>
    private static async Task<Exception> MakeCallerTheDeadlockVictimAsync(
        UpsertStore store,
        UpsertContext context,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task>? pendingCall = null
    )
    {
        using var probeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var probeToken = probeCancellation.Token;
        await ExecuteNativeAsync(store.Observer,
            "CREATE TABLE `UpsertDeadlockWeight` (`Id` int NOT NULL PRIMARY KEY) ENGINE=InnoDB;",
            probeToken);

        await using var holder = new MySqlConnection(store.ConnectionString);
        await holder.OpenAsync(probeToken);
        await using var holderTransaction = await holder.BeginTransactionAsync(probeToken);
        await ExecuteNativeAsync(holder,
            "UPDATE `UpsertRows` SET `Unchanged`='holder' WHERE `Id`=2;",
            probeToken,
            holderTransaction);

        var weight = string.Join(',',
            Enumerable
                .Range(1, 2000)
                .Select(id => "(" + id.ToString(CultureInfo.InvariantCulture) + ")"));

        await ExecuteNativeAsync(holder,
            "INSERT INTO `UpsertDeadlockWeight` VALUES " + weight + ";",
            probeToken,
            holderTransaction);

        var worker = (MySqlConnection)context.Database.GetDbConnection();
        var workerTransaction = (MySqlTransaction)context.Database.CurrentTransaction!.GetDbTransaction();
        await ExecuteNativeAsync(worker,
            "SET SESSION innodb_lock_wait_timeout=30; UPDATE `UpsertRows` SET `Unchanged`='worker' WHERE `Id`=1;",
            probeToken,
            workerTransaction);

        var pending = pendingCall?.Invoke(probeToken)
            ?? ExecuteNativeAsync(worker,
                "UPDATE `UpsertRows` SET `Unchanged`='worker' WHERE `Id`=2;",
                probeToken,
                workerTransaction);

        Exception? primary = null;
        Exception? victim = null;

        try
        {
            await MySqlNativeLockWaitObserver.ObserveAsync(store.Version.IsMariaDb,
                store.Observer,
                worker,
                pending,
                probeToken);

            await ExecuteNativeAsync(holder,
                "UPDATE `UpsertRows` SET `Unchanged`='holder' WHERE `Id`=1;",
                probeToken,
                holderTransaction);

            victim = await Record.ExceptionAsync(async () => await pending);
            primary = victim;
        }
        catch (Exception error)
        {
            primary = error;

            throw;
        }
        finally
        {
            // WHY: Fixture teardown must release the holder and drain the real native task
            // even when lock observation or victim selection fails; no delay proves readiness.
            await DatabaseLockProbeCleanup.CompleteLockProbeAsync(probeCancellation,
                () => holderTransaction.RollbackAsync(CancellationToken.None),
                pending,
                primary,
                null);
        }

        return victim
            ?? throw new InvalidOperationException("The controlled native deadlock did not produce its victim error.");
    }
}
