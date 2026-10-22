namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlColumnDefaultIntegrationTests
{
    private static async Task ExecuteAsync(
        IMigrationsSqlGenerator generator,
        IModel model,
        IRelationalConnection connection,
        IReadOnlyList<MigrationOperation> operations
    )
    {
        foreach (var command in generator.Generate(operations, model))
        {
            _ = await command
                .ExecuteNonQueryAsync(
                    connection,
                    cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
        }
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
}
