using System.Transactions;
using IsolationLevel = System.Data.IsolationLevel;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlGeneratedValueReadbackTests
{
    /// <summary>
    /// Reads inserted rows from the transaction's own writes after an older repeatable-read snapshot.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task ExistingRepeatableReadSnapshotStillReadsOwnGeneratedValues()
    {
        // Arrange
        await using var context = CreateContext<int, StandardMode>(IntegrationDatabaseTarget.MySql84);
        await RecreateTableAsync(context, CancellationToken.None);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            CancellationToken.None);

        _ = await context
            .Rows
            .AsNoTracking()
            .CountAsync(CancellationToken.None);

        await using var other = new MySqlConnection(context.Database.GetConnectionString());
        await other.OpenAsync(CancellationToken.None);
        await using var seed = other.CreateCommand();
        seed.CommandText = $"INSERT INTO `{TableName}` (`Id`, `Input`) VALUES (9000, 9000);";
        _ = await seed.ExecuteNonQueryAsync(CancellationToken.None);

        var rows = CreateIntegerRows(64);
        AddRows(context, rows);

        // Act
        await context.SaveChangesAsync(CancellationToken.None);
        var visibleCount = await context
            .Rows
            .AsNoTracking()
            .CountAsync(CancellationToken.None);

        // Assert
        Assert.All(rows, AssertGenerated);
        Assert.Equal(64, visibleCount);
    }

    /// <summary>Retains generated readback inside an ambient caller transaction.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task NeverWithAmbientTransactionCanBatchReadback()
    {
        // Arrange
        var capture = new ReadbackCapture();
        await using var context = CreateContext<int, StandardMode>(IntegrationDatabaseTarget.MySql84, capture);
        await RecreateTableAsync(context, CancellationToken.None);
        context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;

        var rows = CreateIntegerRows(8);
        AddRows(context, rows);
        capture.Enabled = true;

        // Act
        await SaveInAmbientTransactionAsync(context, CancellationToken.None);

        // Assert
        Assert.All(rows, AssertGenerated);
        Assert.Equal(7, capture.Commands.Sum(sql => CountOccurrences(sql, "UNION ALL")));
    }

    /// <summary>Retains generated readback inside an explicitly enlisted caller transaction.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task NeverWithEnlistedTransactionCanBatchReadback()
    {
        // Arrange
        var capture = new ReadbackCapture();
        await using var context = CreateContext<int, StandardMode>(IntegrationDatabaseTarget.MySql84, capture);
        await RecreateTableAsync(context, CancellationToken.None);
        context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
        using var transaction = new CommittableTransaction();
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        context.Database.EnlistTransaction(transaction);

        var rows = CreateIntegerRows(8);
        AddRows(context, rows);
        capture.Enabled = true;

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        Assert.All(rows, AssertGenerated);
        Assert.Equal(7, capture.Commands.Sum(sql => CountOccurrences(sql, "UNION ALL")));
    }

    /// <summary>A concurrent update cannot replace an inserted row before readback completes.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task ConcurrentUpdateWaitsForGeneratedReadbackTransaction() =>
        AssertConcurrentWriterWaitsAsync(delete: false, cancellationToken: CancellationToken.None);

    /// <summary>A concurrent delete cannot remove an inserted row before readback completes.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task ConcurrentDeleteWaitsForGeneratedReadbackTransaction() =>
        AssertConcurrentWriterWaitsAsync(delete: true, cancellationToken: CancellationToken.None);

    /// <summary>Verifies a competing write waits until generated-value readback completes.</summary>
    /// <param name="delete">The delete used by this operation.</param>
    /// <param name="cancellationToken">The token to observe during asynchronous work.</param>
    private static async Task AssertConcurrentWriterWaitsAsync(
        bool delete,
        CancellationToken cancellationToken
    )
    {
        // Arrange
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var capture = new ReadbackCapture
        {
            AfterExecution = async (command, reader, token) =>
            {
                if (command.CommandText.Contains("UNION ALL", StringComparison.Ordinal))
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
                }

                return reader;
            },
        };

        await using var context = CreateContext<int, StandardMode>(IntegrationDatabaseTarget.MySql84, capture);
        await RecreateTableAsync(context, cancellationToken);
        var rows = CreateIntegerRows(8);
        AddRows(context, rows);
        await using var contender = new MySqlConnection(context.Database.GetConnectionString());
        await contender.OpenAsync(cancellationToken);
        await using var competing = contender.CreateCommand();
        competing.CommandTimeout = 20;
        competing.CommandText = delete
            ? $"DELETE FROM `{TableName}` WHERE `Id` = 1;"
            : $"UPDATE `{TableName}` SET `Input` = 100 WHERE `Id` = 1;";

        capture.Enabled = true;
        var observedWait = false;
        var completedBeforeRelease = false;
        Task<int>? competingWrite = null;

        // Act
        var save = context.SaveChangesAsync(cancellationToken);

        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken: cancellationToken);
            competingWrite = competing.ExecuteNonQueryAsync(cancellationToken);
            observedWait = await WaitForNativeLockAsync(
                context.Database.GetConnectionString()!,
                contender.ServerThread,
                cancellationToken);

            completedBeforeRelease = competingWrite.IsCompleted;
        }
        finally
        {
            release.TrySetResult();
            await save;
        }

        var competingRows = await competingWrite;

        // Assert
        Assert.True(observedWait, "The competing native transaction never entered LOCK WAIT.");
        Assert.False(completedBeforeRelease);
        Assert.Equal(1, competingRows);
        Assert.All(rows, AssertGenerated);
    }

    /// <summary>Polls native lock metadata until the competing write blocks or its deadline expires.</summary>
    /// <param name="connectionString">The connection string used by this operation.</param>
    /// <param name="threadId">The thread id used by this operation.</param>
    /// <param name="cancellationToken">The token to observe during asynchronous work.</param>
    private static async Task<bool> WaitForNativeLockAsync(
        string connectionString,
        long threadId,
        CancellationToken cancellationToken
    )
    {
        await using var observer = new MySqlConnection(connectionString);
        await observer.OpenAsync(cancellationToken);
        await using var command = observer.CreateCommand();

        // WHY: data_lock_waits is live; INNODB_TRX can show an older snapshot
        // while this observer polls a short-lived blocked statement.
        command.CommandText = "SELECT COUNT(*) FROM performance_schema.data_lock_waits AS waits "
            + "JOIN performance_schema.threads AS threads ON threads.THREAD_ID = waits.REQUESTING_THREAD_ID "
            + "WHERE threads.PROCESSLIST_ID = @thread;";

        command.Parameters.AddWithValue("@thread", threadId);

        // WHY: The observer deadline bounds polling without swallowing cancellation requested by its caller.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));

        try
        {
            while (!deadline.IsCancellationRequested)
            {
                var waiting = Convert.ToInt32(
                    await command.ExecuteScalarAsync(deadline.Token),
                    CultureInfo.InvariantCulture);

                if (waiting != 0)
                {
                    return true;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(50), deadline.Token);
            }
        }
        catch (OperationCanceledException) when (
            deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // WHY: The bounded observer reports a missing lock wait to the assertion.
        }

        cancellationToken.ThrowIfCancellationRequested();

        return false;
    }

    /// <summary>Saves the context inside a completed ambient transaction.</summary>
    /// <param name="context">The context used for this operation.</param>
    /// <param name="cancellationToken">The token to observe during asynchronous work.</param>
    private static async Task SaveInAmbientTransactionAsync(
        DbContext context,
        CancellationToken cancellationToken
    )
    {
        using var transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
        await context.SaveChangesAsync(cancellationToken);
        transaction.Complete();
    }
}
