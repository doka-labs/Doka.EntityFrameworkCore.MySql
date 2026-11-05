namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlColumnDefaultIntegrationTests
{
    /// <summary>
    /// Execute for the integration contract.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel setup and execution.</param>
    /// <param name="generator">The generator.</param>
    /// <param name="model">The model.</param>
    /// <param name="connection">The open database connection.</param>
    /// <param name="operations">The migration operations to execute.</param>
    private static async Task ExecuteAsync(
        IMigrationsSqlGenerator generator,
        IModel model,
        IRelationalConnection connection,
        IReadOnlyList<MigrationOperation> operations,
        CancellationToken cancellationToken
    )
    {
        foreach (var command in generator.Generate(operations, model))
        {
            _ = await command
                .ExecuteNonQueryAsync(
                    connection,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Executes raw for the integration contract.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel setup and execution.</param>
    /// <param name="connection">The open database connection.</param>
    /// <param name="sql">The sql.</param>
    private static async Task ExecuteRawAsync(
        MySqlConnection connection,
        string sql,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        _ = await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
