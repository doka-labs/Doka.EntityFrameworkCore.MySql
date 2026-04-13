namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

internal static class IntegrationDatabaseUtilities
{
    public static async Task EnsureDatabaseExistsAsync(
        string connectionString
    )
    {
        var builder = new MySqlConnectionStringBuilder(connectionString);
        var databaseName = builder.Database;

        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException("The integration-test connection string must contain a database name.");
        }

        builder.Database = string.Empty;

        await using var connection = new MySqlConnection(builder.ConnectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE IF NOT EXISTS `{databaseName}`;";

        await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }
}
