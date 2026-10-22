namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlColumnDefaultIntegrationTests
{
    /// <summary>A failed migration preserves history through each public migration entry point.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(RuntimeFailureCases))]
    public Task Failed_runtime_migration_preserves_history(
        string targetName,
        bool useDatabaseFacade
    ) => AssertRuntimeFailureForTargetAsync(Enum.Parse<IntegrationDatabaseTarget>(targetName), useDatabaseFacade);

    private static async Task AssertRuntimeFailureForTargetAsync(
        IntegrationDatabaseTarget target,
        bool useDatabaseFacade
    )
    {
        await using var connection = CreateConnection(target);
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await AssertFailedRuntimeMigrationDoesNotAdvanceHistoryAsync(
                connection,
                IntegrationTestEnvironment.GetServerVersion(target),
                useDatabaseFacade)
            .ConfigureAwait(false);
    }

    private static async Task AssertFailedRuntimeMigrationDoesNotAdvanceHistoryAsync(
        MySqlConnection connection,
        MySqlServerVersion serverVersion,
        bool useDatabaseFacade
    )
    {
        // Arrange
        await DropTimestampRepairMigrationObjectsAsync(connection)
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
                    context.Database.MigrateAsync(CancellationToken.None))
                : await Assert.ThrowsAsync<InvalidOperationException>(() => context
                    .GetService<IMigrator>()
                    .MigrateAsync(cancellationToken: CancellationToken.None));

            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT GROUP_CONCAT(`MigrationId` ORDER BY `MigrationId`) "
                + $"FROM `{TimestampRepairMigrationContract.HistoryTable}`;";

            var appliedMigration = Convert.ToString(
                    await command
                        .ExecuteScalarAsync(CancellationToken.None)
                        .ConfigureAwait(false),
                    CultureInfo.InvariantCulture);

            command.CommandText = "SELECT COUNT(*) "
                + $"FROM `{TimestampRepairMigrationContract.Table}` "
                + "WHERE `OccurredAt` IS NULL;";

            var remainingNulls = Convert.ToInt64(
                    await command
                        .ExecuteScalarAsync(CancellationToken.None)
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
            await DropTimestampRepairMigrationObjectsAsync(connection)
                .ConfigureAwait(false);
        }
    }

    private static async Task DropTimestampRepairMigrationObjectsAsync(
        MySqlConnection connection
    )
    {
        await ExecuteRawAsync(connection, $"DROP TABLE IF EXISTS `{TimestampRepairMigrationContract.Table}`;")
            .ConfigureAwait(false);
        await ExecuteRawAsync(connection, $"DROP TABLE IF EXISTS `{TimestampRepairMigrationContract.HistoryTable}`;")
            .ConfigureAwait(false);
    }
}
