namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

internal static class IntegrationDatabaseUtilities
{
    /// <summary>
    /// Returns a connection string scoped to an isolated integration-test database.
    /// </summary>
    /// <param name="baseConnectionString">Connection string for the target server.</param>
    /// <param name="databaseName">Name of the isolated database.</param>
    /// <returns>A connection string that selects <paramref name="databaseName"/>.</returns>
    public static string BuildConnectionString(
        string baseConnectionString,
        string databaseName
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseConnectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        var builder = new MySqlConnectionStringBuilder(baseConnectionString)
        {
            Database = databaseName,
        };

        return builder.ConnectionString;
    }

    /// <summary>
    /// Creates the database selected by the supplied connection string when it
    /// does not already exist.
    /// </summary>
    /// <param name="connectionString">Connection string selecting the database.</param>
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
        command.CommandText = $"CREATE DATABASE IF NOT EXISTS {DelimitIdentifier(databaseName)};";

        await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Drops the database selected by the supplied connection string.
    /// </summary>
    /// <param name="connectionString">Connection string selecting the database.</param>
    public static async Task DropDatabaseAsync(
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
        command.CommandText = $"DROP DATABASE IF EXISTS {DelimitIdentifier(databaseName)};";

        await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    private static string DelimitIdentifier(
        string identifier
    ) => $"`{identifier.Replace("`", "``", StringComparison.Ordinal)}`";
}
