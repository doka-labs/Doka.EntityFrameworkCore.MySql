namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlCteGraphIntegrationTests
{
    /// <summary>CTE-N21 preserves an actual engine-aborted caller transaction instead of retrying or claiming savepoint repair.</summary>
    [RequiresDatabaseTargetsTheory(IntegrationDatabaseTarget.MySql84,
        IntegrationDatabaseTarget.MySql97,
        IntegrationDatabaseTarget.MariaDb123)]
    [MemberData(nameof(SupportedMutationTargets))]
    public async Task Native_cte_deadlock_preserves_the_engine_abort_and_rolls_back_prior_caller_work(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(safety.Token);
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        await store.ExecuteAsync("CREATE TABLE GraphCteDeadlockWeight(Id INT NOT NULL PRIMARY KEY) ENGINE=InnoDB;",
            safety.Token);

        await using var holder = await store.OpenNativeAsync(safety.Token);
        await using var holderTransaction = await holder.BeginTransactionAsync(safety.Token);
        await ExecuteGraphNativeAsync(holder,
            holderTransaction,
            "UPDATE GraphVertices SET Name='holder' WHERE Id=2;",
            safety.Token);

        var weight = string.Join(',',
            Enumerable
                .Range(1, 2000)
                .Select(id => "(" + id.ToString(CultureInfo.InvariantCulture) + ")"));

        await ExecuteGraphNativeAsync(holder,
            holderTransaction,
            "INSERT INTO GraphCteDeadlockWeight VALUES " + weight + ";",
            safety.Token);

        var commands = new GraphCommands();
        await using var context = store.CreateContext(commands, pooled: false);
        await context.Database.OpenConnectionAsync(safety.Token);
        await using var transaction = await context.Database.BeginTransactionAsync(safety.Token);
        var worker = (MySqlConnection)context.Database.GetDbConnection();
        var nativeTransaction = (MySqlTransaction)transaction.GetDbTransaction();
        await ExecuteGraphNativeAsync(worker,
            nativeTransaction,
            "UPDATE GraphVertices SET Name='prior-caller-work' WHERE Id=1;",
            safety.Token);

        var query = context
            .Facets
            .Where(row => row.Id == 2)
            .Select(row => row.Id)
            .WithCte(ids => context.Vertices.Where(row => ids.Contains(row.Id)));

        Exception? primary = null;
        Exception? observed = null;

        // Act
        var pending = query.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Name, "worker"),
            cancellation.Token);

        try
        {
            await MySqlNativeLockWaitObserver.ObserveAsync(store.Version.IsMariaDb,
                store.Observer,
                worker,
                pending,
                safety.Token);

            await ExecuteGraphNativeAsync(holder,
                holderTransaction,
                "UPDATE GraphVertices SET Name='holder' WHERE Id=1;",
                safety.Token);

            observed = await Record.ExceptionAsync(() => pending);
            primary = observed;
        }
        catch (Exception failure)
        {
            primary = failure;
            throw;
        }
        finally
        {
            // WHY: Native victim selection is based on undo weight and observed blocking,
            // not scheduling delays. Release the surviving owner and observe the failed task.
            await DatabaseLockProbeCleanup.CompleteLockProbeAsync(cancellation,
                () => holderTransaction.RollbackAsync(CancellationToken.None),
                pending,
                primary,
                null);
        }

        var names = await store.ReadScalarAsync(
            "SELECT GROUP_CONCAT(Name ORDER BY Id) FROM GraphVertices WHERE Id IN (1,2);",
            safety.Token);

        // Assert
        Assert.Equal(1213,
            Assert.IsType<MySqlException>(observed)
                .Number);

        Assert.Equal("one,two", names);
        Assert.Single(commands.Writes);
        Assert.Empty(commands.Reads);
        Assert.Same(transaction, context.Database.CurrentTransaction);
        Assert.True(pending.IsCompleted);
    }

    /// <summary>Executes fixture-owned native work in the explicitly owned transaction.</summary>
    private static async Task ExecuteGraphNativeAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken
    )
    {
        await using var command = new MySqlCommand(sql, connection, transaction);
        _ = await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
