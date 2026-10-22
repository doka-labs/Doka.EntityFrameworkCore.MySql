namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Observes lock waits without depending on process-list text encoding.</summary>
public sealed partial class MySqlDistributedCacheIntegrationTests
{
    /// <summary>
    /// Binary process text cannot make the lock probe fail or conceal its ASCII command match.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public async Task Locking_probe_handles_binary_process_text_without_losing_its_match()
    {
        // Arrange
        await using var connection = new MySqlConnection(
            IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql97));
        await connection.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = GetLockingStatementProbeSql(
            "(SELECT CONNECTION_ID() + 1 AS ID, @statement AS INFO) AS process");
        var statement = Encoding
            .ASCII
            .GetBytes("UPDATE `DokaProbe` SET `Value` = ")
            .Concat(
                new byte[]
                {
                    0x88,
                    0x99,
                    0xAA,
                    0xBB,
                    0xCC,
                    0xDD
                })
            .ToArray();
        command.Parameters.AddWithValue("@statement", statement);
        command.Parameters.AddWithValue("@tableName", "DokaProbe");
        command.Parameters.AddWithValue("@cleanupOnly", false);

        // Act
        var waiting = Convert.ToInt64(
            await command.ExecuteScalarAsync(CancellationToken.None),
            CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(1, waiting);
    }

    /// <summary>
    /// Binary process text also remains searchable while observing a table-lock wait.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public async Task Table_lock_probe_handles_binary_process_text_without_losing_its_match()
    {
        // Arrange
        await using var connection = new MySqlConnection(
            IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql97));
        await connection.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = GetWaitingForTableLockProbeSql(
            "(SELECT CONNECTION_ID() + 1 AS ID, @statement AS INFO, "
            + "'Waiting for table metadata lock' AS STATE) AS process");
        var statement = Encoding
            .ASCII
            .GetBytes("ALTER TABLE `DokaProbe` ADD `Value` int ")
            .Concat(
                new byte[]
                {
                    0x88,
                    0x99,
                    0xAA,
                    0xBB,
                    0xCC,
                    0xDD
                })
            .ToArray();
        command.Parameters.AddWithValue("@statement", statement);
        command.Parameters.AddWithValue("@tableName", "DokaProbe");

        // Act
        var waiting = Convert.ToInt64(
            await command.ExecuteScalarAsync(CancellationToken.None),
            CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(1, waiting);
    }
    /// <summary>Waits for the expected command to reach a real database row lock.</summary>
    private static async Task AssertLockingStatementStartedAsync(
        MySqlConnection observer,
        string tableName,
        Task pending,
        bool cleanupOnly = false
    )
    {
        await using var command = observer.CreateCommand();

        // WHY: RemoveAsync and cleanup both issue DELETE statements. The cleanup-only predicate requires the
        // cleanup command's primary-index clause so cancellation never interrupts foreground removal instead.
        // Unique-key locking reads can wait in optimization before INNODB_TRX exposes the wait.
        command.CommandText = GetLockingStatementProbeSql("information_schema.PROCESSLIST");

        command.Parameters.AddWithValue("@tableName", tableName);
        command.Parameters.AddWithValue("@cleanupOnly", cleanupOnly);

        var timeout = Stopwatch.StartNew();

        while (timeout.Elapsed < TimeSpan.FromSeconds(15))
        {
            Assert.False(pending.IsCompleted, "The cache operation completed before it reached the row lock.");

            var waiting = Convert.ToInt64(
                await command
                    .ExecuteScalarAsync(CancellationToken.None)
                    .ConfigureAwait(false),
                CultureInfo.InvariantCulture);

            if (waiting > 0)
            {
                return;
            }

            await Task
                .Delay(10, CancellationToken.None)
                .ConfigureAwait(false);
        }

        Assert.Fail("The cache operation did not reach the database row lock within the test deadline.");
    }

    // WHY: PROCESSLIST.INFO can expose command bytes that are not valid in its
    // metadata charset. Interpret the bytes without loss before searching for
    // ASCII SQL tokens, so an unrelated request cannot break the lock probe.
    private static string GetLockingStatementProbeSql(
        string processSource
    ) => $"""
           SELECT COUNT(*) FROM (
               SELECT ID, CONVERT(CAST(INFO AS BINARY) USING latin1) AS StatementText
               FROM {processSource}
           ) AS observed
           WHERE ID <> CONNECTION_ID()
               AND LOCATE(@tableName, COALESCE(StatementText, '')) > 0
               AND (UPPER(StatementText) LIKE '%FOR UPDATE%'
                   OR UPPER(LTRIM(StatementText)) LIKE 'UPDATE %'
                   OR UPPER(LTRIM(StatementText)) LIKE 'DELETE %')
               AND (@cleanupOnly = FALSE
                   OR UPPER(LTRIM(StatementText)) LIKE 'DELETE % FROM % FORCE INDEX (PRIMARY)%');
           """;

    /// <summary>Waits for the selected cache command to block on a table lock.</summary>
    private static async Task AssertWaitingForTableLockAsync(
        MySqlConnection observer,
        string tableName,
        Task pending
    )
    {
        await using var command = observer.CreateCommand();
        command.CommandText = GetWaitingForTableLockProbeSql("information_schema.PROCESSLIST");

        command.Parameters.AddWithValue("@tableName", tableName);

        var timeout = Stopwatch.StartNew();

        while (timeout.Elapsed < TimeSpan.FromSeconds(15))
        {
            Assert.False(pending.IsCompleted, "The cache operation completed before it reached the table lock.");

            var waiting = Convert.ToInt64(
                await command
                    .ExecuteScalarAsync(CancellationToken.None)
                    .ConfigureAwait(false),
                CultureInfo.InvariantCulture);

            if (waiting > 0)
            {
                return;
            }

            await Task
                .Delay(10, CancellationToken.None)
                .ConfigureAwait(false);
        }

        Assert.Fail("The cache operation did not reach the database table lock within the test deadline.");
    }

    // WHY: This observer can encounter the same undecodable process text as
    // the row-lock probe while another test sends binary command parameters.
    private static string GetWaitingForTableLockProbeSql(
        string processSource
    ) => $"""
           SELECT COUNT(*) FROM (
               SELECT ID, CONVERT(CAST(INFO AS BINARY) USING latin1) AS StatementText, STATE
               FROM {processSource}
           ) AS observed
           WHERE ID <> CONNECTION_ID()
               AND LOCATE(@tableName, COALESCE(StatementText, '')) > 0
               AND LOWER(COALESCE(STATE, '')) LIKE '%lock%';
           """;
}
