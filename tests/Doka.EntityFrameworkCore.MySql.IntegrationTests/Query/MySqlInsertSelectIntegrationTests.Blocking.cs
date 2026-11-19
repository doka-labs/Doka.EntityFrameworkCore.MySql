namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlInsertSelectIntegrationTests
{
    /// <summary>A blocked native insert observes the actual caller token and is fully drained before reuse.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Cancellation_of_server_observed_blocked_insert_drains_owned_work(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(safety.Token);
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        await ExecuteNativeAsync(store.Observer,
            $"INSERT INTO `{TargetTable}` (`Id`,`Value`,`order`) VALUES (1,99,'held');",
            safety.Token);

        await using var holder = await store.OpenNativeAsync(safety.Token);
        await using var heldTransaction = await holder.BeginTransactionAsync(safety.Token);
        await ExecuteNativeAsync(holder,
            $"UPDATE `{TargetTable}` SET `Value`=100 WHERE `Id`=1;",
            safety.Token,
            heldTransaction);

        var commands = new InsertCommands();
        await using var context = store.CreateContext(commands);
        await context.Database.OpenConnectionAsync(safety.Token);
        var worker = (MySqlConnection)context.Database.GetDbConnection();
        Exception? primary = null;
        Exception? observed = null;

        // Act
        var pending = context
            .Set<InsertSource>()
            .Where(row => row.Id == 1)
            .ExecuteInsertFromQueryAsync(context.Set<InsertTarget>(),
                setters => setters
                    .SetProperty(row => row.Id, row => row.Id)
                    .SetProperty(row => row.Code, row => row.Label)
                    .SetProperty(row => row.Value, row => row.Value),
                cancellation.Token);

        try
        {
            await MySqlNativeLockWaitObserver.ObserveAsync(store.Version.IsMariaDb,
                store.Observer,
                worker,
                pending,
                safety.Token);

            await cancellation.CancelAsync();
            observed = await Record.ExceptionAsync(async () => _ = await pending);
            primary = observed;
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

        var actual =
            await ReadNativeAsync(store.Observer, $"SELECT `Value`,`order` FROM `{TargetTable}`;", safety.Token);

        var healthy = await context
            .Set<InsertSource>()
            .CountAsync(safety.Token);

        // Assert
        Assert.Equal(cancellation.Token,
            Assert.IsAssignableFrom<OperationCanceledException>(observed)
                .CancellationToken);

        Assert.True(pending.IsCompleted);
        Assert.Equal(["99|held"], actual);
        Assert.Equal(3, healthy);
        Assert.Equal(cancellation.Token, Assert.Single(commands.Tokens));
        Assert.Single(commands.Statements);

        // WHY: The held transaction must release even when forward cancellation is already signaled.
        async Task ReleaseAsync() => await heldTransaction.RollbackAsync(CancellationToken.None);
    }

    /// <summary>A real native deadlock can roll back the whole caller transaction, not merely this insertion.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Native_deadlock_rolls_back_the_victim_transaction_without_retry_or_savepoint_repair(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(safety.Token);
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        var values = string.Join(',',
            Enumerable
                .Range(1, 102)
                .Select(id =>
                    $"({id.ToString(CultureInfo.InvariantCulture)},100,'existing')"));

        await ExecuteNativeAsync(store.Observer,
            $"INSERT INTO `{TargetTable}` (`Id`,`Value`,`order`) VALUES {values}; "
            + "CREATE TABLE InsertOperationDeadlockWeight (`Id` INT PRIMARY KEY) ENGINE=InnoDB;",
            safety.Token);

        await using var holder = await store.OpenNativeAsync(safety.Token);
        await using var heldTransaction = await holder.BeginTransactionAsync(safety.Token);

        // WHY: A range UPDATE excluding PK2 still locks that boundary on qualified MariaDB.
        // Exact PK1 plus undo weight in another table leaves the worker's PK2 setup independent.
        // Native lock observation and 1213 prove the actual cycle/victim, never elapsed time.
        await ExecuteNativeAsync(holder,
            $"UPDATE `{TargetTable}` SET `Value`=200 WHERE `Id`=1;",
            safety.Token,
            heldTransaction);

        var weight = string.Join(',',
            Enumerable
                .Range(1, 2000)
                .Select(id => "(" + id.ToString(CultureInfo.InvariantCulture) + ")"));

        await ExecuteNativeAsync(holder,
            "INSERT INTO InsertOperationDeadlockWeight VALUES " + weight + ";",
            safety.Token,
            heldTransaction);

        var commands = new InsertCommands();
        await using var context = store.CreateContext(commands);
        await using var transaction = await context.Database.BeginTransactionAsync(safety.Token);
        await context.Database.ExecuteSqlRawAsync($"UPDATE `{TargetTable}` SET `Value`=300 WHERE `Id`=2;",
            safety.Token);

        commands.Statements.Clear();
        commands.Parameters.Clear();
        commands.Connections.Clear();
        commands.Tokens.Clear();
        var worker = (MySqlConnection)context.Database.GetDbConnection();
        Exception? primary = null;
        Exception? observed = null;
        Exception? missingMarker = null;

        // Act
        var pending = context
            .Set<InsertSource>()
            .Where(row => row.Id == 1)
            .ExecuteInsertFromQueryAsync(context.Set<InsertTarget>(),
                setters => setters
                    .SetProperty(row => row.Id, row => row.Id)
                    .SetProperty(row => row.Code, row => row.Label)
                    .SetProperty(row => row.Value, row => row.Value),
                cancellation.Token);

        try
        {
            await MySqlNativeLockWaitObserver.ObserveAsync(store.Version.IsMariaDb,
                store.Observer,
                worker,
                pending,
                safety.Token);

            await ExecuteNativeAsync(holder,
                $"UPDATE `{TargetTable}` SET `Value`=201 WHERE `Id`=2;",
                safety.Token,
                heldTransaction);

            observed = await Record.ExceptionAsync(async () => _ = await pending);
            primary = observed;
            var nativeTransaction = (MySqlTransaction)transaction.GetDbTransaction();

            // WHY: A newly acknowledged SAVEPOINT alone is not proof of an active transaction.
            // ROLLBACK TO that exact marker supplies the portable native 1305 proof after full rollback.
            await ExecuteNativeAsync(worker, "SAVEPOINT insert_deadlock_state;", safety.Token, nativeTransaction);
            missingMarker = await Record.ExceptionAsync(() => ExecuteNativeAsync(worker,
                "ROLLBACK TO SAVEPOINT insert_deadlock_state;",
                safety.Token,
                nativeTransaction));
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

        var actual = await ReadNativeAsync(store.Observer,
            $"SELECT `Value` FROM `{TargetTable}` WHERE `Id` IN (1,2) ORDER BY `Id`;",
            safety.Token);

        // Assert
        Assert.Equal(1213,
            Assert.IsType<MySqlException>(observed)
                .Number);

        Assert.Equal(1305,
            Assert.IsType<MySqlException>(missingMarker)
                .Number);

        Assert.Equal(["100", "100"], actual);
        Assert.Single(commands.Statements);
        Assert.True(pending.IsCompleted);

        // WHY: Both transactions need independent, uncancellable cleanup after the server has discarded its victim.
        async Task ReleaseAsync()
        {
            await heldTransaction.RollbackAsync(CancellationToken.None);
            await transaction.RollbackAsync(CancellationToken.None);
        }
    }
}
