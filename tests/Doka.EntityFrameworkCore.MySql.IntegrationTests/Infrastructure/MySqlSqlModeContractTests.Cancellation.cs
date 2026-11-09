namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Qualifies cancellation while a scoped migration holds a live database session.</summary>
public sealed partial class MySqlSqlModeContractTests
{
    /// <summary>Verifies cancellation and session restoration using native metadata observation.</summary>
    private static Task AssertCanceledMigrationRestoresSessionAsync(
        string connectionString,
        MySqlConnection connection,
        DbContext context,
        CancellationToken cancellationToken
    ) => AssertCanceledMigrationRestoresSessionAsync(
        connectionString,
        connection,
        context,
        WaitForMetadataLockAsync,
        cancellationToken);

    /// <summary>Owns the blocked command through observation, cancellation, restoration, and protected teardown.</summary>
    /// <param name="connectionString">A nonpooled connection string for the test-owned schema.</param>
    /// <param name="connection">The caller session whose SQL mode and reuse are verified.</param>
    /// <param name="context">The provider context bound to the caller session.</param>
    /// <param name="observeLock">The observation boundary used to reproduce independent observer failures.</param>
    /// <param name="cancellationToken">The token observed while arranging and exercising the probe.</param>
    private static async Task AssertCanceledMigrationRestoresSessionAsync(
        string connectionString,
        MySqlConnection connection,
        DbContext context,
        Func<MySqlConnection, int, CancellationToken, Task> observeLock,
        CancellationToken cancellationToken
    )
    {
        // Arrange
        const string expectedSqlMode = "ANSI_QUOTES,STRICT_TRANS_TABLES";
        var tableName = $"SqlModeCancellation_{Guid.NewGuid():N}";
        await using var blocker = new MySqlConnection(connectionString);
        await using var observer = new MySqlConnection(connectionString);
        await blocker
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await observer
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await SetSqlModeAsync(connection, expectedSqlMode, cancellationToken)
            .ConfigureAwait(false);

        await using (var createTable = connection.CreateCommand())
        {
            createTable.CommandText = $"CREATE TABLE `{tableName}` (`Id` int NOT NULL);";
            _ = await createTable
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        // WHY: The source must survive an observer failure; disposing a CTS does not cancel its command.
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task? execution = null;
        Exception? primaryFailure = null;
        var executionObserved = false;

        try
        {
            await using (var lockCommand = blocker.CreateCommand())
            {
                lockCommand.CommandText = $"LOCK TABLES `{tableName}` WRITE;";
                _ = await lockCommand
                    .ExecuteNonQueryAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            var generator = context.GetService<IMigrationsSqlGenerator>();
            var relationalConnection = context.GetService<IRelationalConnection>();
            var operation = new AddColumnOperation
            {
                Table = tableName,
                Name = "Value",
                ClrType = typeof(string),
                ColumnType = "varchar(64)",
                IsNullable = true,
                Comment = "path\\segment",
            };

            var migrationCommand = Assert.Single(generator.Generate([operation], context.Model));

            // Act
            execution = migrationCommand.ExecuteNonQueryAsync(
                relationalConnection,
                cancellationToken: cancellation.Token);

            await observeLock(observer, connection.ServerThread, cancellationToken)
                .ConfigureAwait(false);

            await cancellation
                .CancelAsync()
                .ConfigureAwait(false);

            // WHY: A deadline may fail the assertion, but finally still closes the blocker and drains the task.
            _ = await Assert
                .ThrowsAnyAsync<OperationCanceledException>(async () =>
                    await execution.ConfigureAwait(false))
                .WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None)
                .ConfigureAwait(false);

            // WHY: Expected cancellation has been fully observed and must not become a second teardown failure.
            executionObserved = true;

            // Assert
            Assert.Equal(
                expectedSqlMode,
                await ReadSqlModeAsync(connection, cancellationToken)
                    .ConfigureAwait(false));

            await using var verify = connection.CreateCommand();
            verify.CommandText = "SELECT 1;";
            Assert.Equal(
                1,
                Convert.ToInt32(
                    await verify
                        .ExecuteScalarAsync(cancellationToken)
                        .ConfigureAwait(false),
                    CultureInfo.InvariantCulture));
        }
        catch (Exception failure)
        {
            primaryFailure = failure;

            throw;
        }
        finally
        {
            // WHY: Closing this nonpooled session releases all table locks even when observation or UNLOCK fails.
            // The shared boundary observes the complete migration, including SQL-mode restoration, before DROP.
            await DatabaseLockProbeCleanup
                .CompleteLockProbeAsync(
                    cancellation,
                    () => blocker.CloseAsync(),
                    executionObserved
                        ? Task.CompletedTask
                        : execution ?? Task.CompletedTask,
                    primaryFailure,
                    () => DropSqlModeProbeTableAsync(connection, tableName))
                .ConfigureAwait(false);
        }
    }

    /// <summary>Waits for the exact migration session to reach its table-metadata lock boundary.</summary>
    private static async Task WaitForMetadataLockAsync(
        MySqlConnection observer,
        int connectionId,
        CancellationToken cancellationToken
    )
    {
        var timeout = Stopwatch.StartNew();

        while (timeout.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (await IsWaitingForMetadataLockAsync(observer, connectionId, cancellationToken)
                .ConfigureAwait(false))
            {
                return;
            }

            await Task
                .Delay(TimeSpan.FromMilliseconds(25), cancellationToken)
                .ConfigureAwait(false);
        }

        throw new TimeoutException("The migration command did not enter the expected metadata-lock wait state.");
    }

    /// <summary>Reads only the requested native session and rejects idle or unrelated sessions.</summary>
    private static async Task<bool> IsWaitingForMetadataLockAsync(
        MySqlConnection observer,
        int connectionId,
        CancellationToken cancellationToken
    )
    {
        // WHY: MySQL fills INFORMATION_SCHEMA.PROCESSLIST, including INFO, before filtering;
        // unrelated binary SQL can break STATE-only reads. MariaDB uses a different conversion path.
        var processSource = GetMetadataProcessSource(observer);

        await using var command = observer.CreateCommand();
        command.CommandText = $"SELECT STATE FROM {processSource} WHERE ID = @connectionId;";
        command.Parameters.AddWithValue("@connectionId", connectionId);
        var state = await command
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false) as string;

        return state?.Contains("metadata lock", StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>Selects the process source that preserves native statements for the connected engine family.</summary>
    private static string GetMetadataProcessSource(
        MySqlConnection observer
    ) => observer.ServerVersion.Contains("MariaDB", StringComparison.OrdinalIgnoreCase)
        ? "information_schema.PROCESSLIST"
        : "performance_schema.processlist";

    /// <summary>Drops a probe table only after its command is drained; caller cancellation cannot abandon cleanup.</summary>
    private static async Task DropSqlModeProbeTableAsync(
        MySqlConnection connection,
        string tableName
    )
    {
        await using var dropTable = connection.CreateCommand();
        dropTable.CommandText = $"DROP TABLE IF EXISTS `{tableName}`;";
        _ = await dropTable
            .ExecuteNonQueryAsync(CancellationToken.None)
            .ConfigureAwait(false);
    }
}
