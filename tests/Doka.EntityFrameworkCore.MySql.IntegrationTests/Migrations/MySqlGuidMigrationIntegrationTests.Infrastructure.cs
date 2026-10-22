namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlGuidMigrationIntegrationTests
{
    private static async Task<long> ExecuteLongScalarAsync(
        MySqlConnection connection,
        string sql
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToInt64(
            await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteRawAsync(
        MySqlConnection connection,
        string sql
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        _ = await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static IReadOnlyList<MigrationOperation> GetDifferences(
        DbContext source,
        DbContext target
    ) => target
        .GetService<IMigrationsModelDiffer>()
        .GetDifferences(
            source
                .GetService<IDesignTimeModel>()
                .Model
                .GetRelationalModel(),
            target
                .GetService<IDesignTimeModel>()
                .Model
                .GetRelationalModel());

    private static async Task ExecuteOperationsAsync(
        DbContext context,
        MySqlConnection connection,
        IReadOnlyList<MigrationOperation> operations
    )
    {
        var commands = context
            .GetService<IMigrationsSqlGenerator>()
            .Generate(
                operations,
                context.GetService<IDesignTimeModel>()
                    .Model);

        foreach (var migrationCommand in commands)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = migrationCommand.CommandText;
            _ = await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task CleanupAsync(
        MySqlConnection connection
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "DROP TABLE IF EXISTS `TextGuidDocumentRevisions`; "
            + "DROP TABLE IF EXISTS `TextGuidDocuments`; "
            + $"DROP TABLE IF EXISTS `{StorageTransitionTable}`;";
        _ = await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static DbContextOptions<TContext> CreateOptions<TContext>(
        string connectionString,
        MySqlServerVersion serverVersion,
        MySqlGuidFormat defaultGuidFormat = MySqlGuidFormat.Binary16
    )
        where TContext : DbContext => IntegrationTestDbContextOptions
        .Create<TContext>()
        .UseMySql(
            connectionString,
            serverVersion,
            options => options.DefaultGuidFormat(defaultGuidFormat))
        .Options;
}
