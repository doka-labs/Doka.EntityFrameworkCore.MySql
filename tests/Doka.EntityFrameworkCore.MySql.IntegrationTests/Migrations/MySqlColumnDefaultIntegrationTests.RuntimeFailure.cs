namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlColumnDefaultIntegrationTests
{
    /// <summary>A failed migration preserves history through each public migration entry point.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(RuntimeFailureCases))]
    public Task Failed_runtime_migration_preserves_history(
        string targetName,
        bool useDatabaseFacade
    ) =>
        AssertRuntimeFailureForTargetAsync(
            Enum.Parse<IntegrationDatabaseTarget>(targetName),
            useDatabaseFacade,
            CancellationToken.None);

    /// <summary>
    /// Verifies runtime failure for target for the integration contract.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel setup and execution.</param>
    /// <param name="target">The database target used by the test.</param>
    /// <param name="useDatabaseFacade">The use database facade.</param>
    private static async Task AssertRuntimeFailureForTargetAsync(
        IntegrationDatabaseTarget target,
        bool useDatabaseFacade,
        CancellationToken cancellationToken
    )
    {
        await using var connection = CreateConnection(target);
        await connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await AssertFailedRuntimeMigrationDoesNotAdvanceHistoryAsync(
                connection,
                IntegrationTestEnvironment.GetServerVersion(target),
                useDatabaseFacade,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies failed runtime migration does not advance history for the integration contract.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel setup and execution.</param>
    /// <param name="connection">The open database connection.</param>
    /// <param name="serverVersion">The configured database server version.</param>
    /// <param name="useDatabaseFacade">The use database facade.</param>
    private static async Task AssertFailedRuntimeMigrationDoesNotAdvanceHistoryAsync(
        MySqlConnection connection,
        MySqlServerVersion serverVersion,
        bool useDatabaseFacade,
        CancellationToken cancellationToken
    )
    {
        // Arrange
        await DropTimestampRepairMigrationObjectsAsync(connection, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await using var context = new TimestampRepairMigrationContext(
                IntegrationTestDbContextOptions
                    .Create<TimestampRepairMigrationContext>()
                    .UseMySql(
                        connection,
                        serverVersion,
                        options => options
                            .MigrationsAssembly(typeof(TimestampRepairMigrationContext).Assembly.FullName!)
                            .MigrationsHistoryTable(TimestampRepairMigrationContract.HistoryTable))
                    .Options);

            // Act
            var exception = useDatabaseFacade
                ? await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    context.Database.MigrateAsync(cancellationToken))
                : await Assert.ThrowsAsync<InvalidOperationException>(() => context
                    .GetService<IMigrator>()
                    .MigrateAsync(cancellationToken: cancellationToken));

            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT GROUP_CONCAT(`MigrationId` ORDER BY `MigrationId`) "
                + $"FROM `{TimestampRepairMigrationContract.HistoryTable}`;";

            var appliedMigration = Convert.ToString(
                await command
                    .ExecuteScalarAsync(cancellationToken)
                    .ConfigureAwait(false),
                CultureInfo.InvariantCulture);

            command.CommandText = "SELECT COUNT(*) "
                + $"FROM `{TimestampRepairMigrationContract.Table}` "
                + "WHERE `OccurredAt` IS NULL;";

            var remainingNulls = Convert.ToInt64(
                await command
                    .ExecuteScalarAsync(cancellationToken)
                    .ConfigureAwait(false),
                CultureInfo.InvariantCulture);

            // Assert
            Assert.Contains(nameof(AlterColumnOperation), exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(TimestampRepairMigrationContract.Table, exception.Message, StringComparison.Ordinal);
            Assert.Equal(TimestampRepairMigrationContract.InitialMigration, appliedMigration);
            Assert.Equal(1L, remainingNulls);
        }
        finally
        {
            await DropTimestampRepairMigrationObjectsAsync(connection, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Drops timestamp repair migration objects for the integration contract.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel setup and execution.</param>
    /// <param name="connection">The open database connection.</param>
    private static async Task DropTimestampRepairMigrationObjectsAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken
    )
    {
        await ExecuteRawAsync(
                connection,
                $"DROP TABLE IF EXISTS `{TimestampRepairMigrationContract.Table}`;",
                cancellationToken)
            .ConfigureAwait(false);

        await ExecuteRawAsync(
                connection,
                $"DROP TABLE IF EXISTS `{TimestampRepairMigrationContract.HistoryTable}`;",
                cancellationToken)
            .ConfigureAwait(false);
    }
}
