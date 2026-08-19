namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Implements EF Core database lifecycle operations against the active MySQL-compatible engine.
/// </summary>
internal sealed class MySqlRelationalDatabaseCreator : RelationalDatabaseCreator
{
    private const string HasTablesSql = """
                                        SELECT CASE
                                            WHEN EXISTS (
                                                SELECT 1
                                                FROM information_schema.tables
                                                WHERE table_schema = DATABASE()
                                            ) THEN 1
                                            ELSE 0
                                        END
                                        """;

    public MySqlRelationalDatabaseCreator(
        RelationalDatabaseCreatorDependencies dependencies
    ) : base(dependencies)
    { }

    public override bool Exists()
    {
        // Server-side authoritative existence check via information_schema.SCHEMATA
        // rather than open-with-database. The latter can return TRUE against a
        // pooled MySqlConnector connection cached from a prior session even after
        // the database has been dropped, because the pool's connection-validation
        // path does not re-resolve database existence on checkout. The
        // information_schema query always reaches the server and reflects the
        // current schema state.
        try
        {
            var databaseName = GetDatabaseName();
            using var lease = CreateServerConnection();
            lease.Open();
            var connection = lease.Connection;
            return SchemaExists(connection, databaseName);
        }
        catch (MySqlException exception)
        {
            if (IsMissingDatabaseAccessDenied(exception))
            {
                return false;
            }

            throw;
        }
    }

    public override async Task<bool> ExistsAsync(
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var databaseName = GetDatabaseName();
            await using var lease = CreateServerConnection();
            await lease
                .OpenAsync(cancellationToken)
                .ConfigureAwait(false);
            var connection = lease.Connection;
            return await SchemaExistsAsync(connection, databaseName, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (MySqlException exception)
        {
            if (IsMissingDatabaseAccessDenied(exception))
            {
                return false;
            }

            throw;
        }
    }

    private static bool SchemaExists(
        DbConnection connection,
        string databaseName
    )
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME = @name";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@name";
        parameter.Value = databaseName;
        command.Parameters.Add(parameter);

        var result = command.ExecuteScalar();
        return result is not null && Convert.ToInt64(result, CultureInfo.InvariantCulture) > 0;
    }

    private static async Task<bool> SchemaExistsAsync(
        DbConnection connection,
        string databaseName,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME = @name";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@name";
        parameter.Value = databaseName;
        command.Parameters.Add(parameter);

        var result = await command
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);

        return result is not null && Convert.ToInt64(result, CultureInfo.InvariantCulture) > 0;
    }

    public override void Create()
    {
        var commandText = BuildCreateDatabaseSql();
        using var lease = CreateServerConnection();
        lease.Open();
        var connection = lease.Connection;

        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

    public override async Task CreateAsync(
        CancellationToken cancellationToken = default
    )
    {
        var commandText = BuildCreateDatabaseSql();
        await using var lease = CreateServerConnection();
        await lease
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);
        var connection = lease.Connection;

        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public override void Delete()
    {
        var commandText = BuildDropDatabaseSql();
        using var lease = CreateServerConnection();
        lease.Open();
        var connection = lease.Connection;

        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

    public override async Task DeleteAsync(
        CancellationToken cancellationToken = default
    )
    {
        var commandText = BuildDropDatabaseSql();
        await using var lease = CreateServerConnection();
        await lease
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);
        var connection = lease.Connection;

        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public override bool HasTables()
    {
        using var lease = CreateDatabaseConnection();
        lease.Open();
        var connection = lease.Connection;

        using var command = connection.CreateCommand();
        command.CommandText = HasTablesSql;

        var result = command.ExecuteScalar();

        return MySqlScalarConvert.ToBoolean(result);
    }

    public override async Task<bool> HasTablesAsync(
        CancellationToken cancellationToken = default
    )
    {
        await using var lease = CreateDatabaseConnection();
        await lease
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);
        var connection = lease.Connection;

        await using var command = connection.CreateCommand();
        command.CommandText = HasTablesSql;

        var result = await command
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);

        return MySqlScalarConvert.ToBoolean(result);
    }

    private MySqlLifecycleConnection CreateDatabaseConnection()
        => GetProviderConnection().CreateLifecycleConnection(GetConnectionString());

    private MySqlLifecycleConnection CreateServerConnection()
        => GetProviderConnection().CreateLifecycleConnection(CreateServerConnectionString());

    private MySqlRelationalConnection GetProviderConnection()
        => Dependencies.Connection as MySqlRelationalConnection
            ?? throw new InvalidOperationException(
                "The Doka MySQL database creator requires the provider relational connection.");

    private string BuildCreateDatabaseSql()
        => "CREATE DATABASE IF NOT EXISTS "
            + Dependencies.SqlGenerationHelper.DelimitIdentifier(GetDatabaseName())
            + Dependencies.SqlGenerationHelper.StatementTerminator;

    private string BuildDropDatabaseSql()
        => "DROP DATABASE IF EXISTS "
            + Dependencies.SqlGenerationHelper.DelimitIdentifier(GetDatabaseName())
            + Dependencies.SqlGenerationHelper.StatementTerminator;

    private string CreateServerConnectionString()
    {
        var builder = new MySqlConnectionStringBuilder(GetConnectionString())
        {
            Database = string.Empty,
        };

        builder.Remove("Database");
        builder.Remove("Initial Catalog");

        return builder.ConnectionString;
    }

    private string GetConnectionString()
    {
        var extension = Dependencies.ContextOptions.FindExtension<MySqlOptionsExtension>();

        // EF tooling replaces the active connection string through
        // Database.SetConnectionString(), for example when an operator supplies
        // `efbundle --connection`. RelationalConnection retains that value
        // independently of driver-side redaction after Open(), so it must remain
        // authoritative over the immutable options snapshot.
        var connectionString = Dependencies.Connection.ConnectionString;

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        if (extension?.DataSource is { } dataSource)
        {
            var dataSourceConnectionString = dataSource.ConnectionString;

            if (!string.IsNullOrWhiteSpace(dataSourceConnectionString))
            {
                return dataSourceConnectionString;
            }
        }

        // The options value is the final fallback for custom connection paths that
        // cannot surface an active relational connection string.
        if (extension?.ConnectionString is { } extensionConnectionString
            && !string.IsNullOrWhiteSpace(extensionConnectionString))
        {
            return extensionConnectionString;
        }

        throw new InvalidOperationException(
            "A MySQL connection string is required for database creation operations. "
            + "When using MySqlDataSource, the connection string is derived from the data source.");
    }

    private string GetDatabaseName()
    {
        var builder = new MySqlConnectionStringBuilder(GetConnectionString());

        return string.IsNullOrWhiteSpace(builder.Database)
            ? throw new InvalidOperationException("A database name is required for database creation operations.")
            : builder.Database;
    }

    private static bool IsMissingDatabase(
        MySqlException exception
    )
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception.ErrorCode == MySqlErrorCode.NoSuchDb || exception.Number == 1049;
    }

    private static bool IsMissingDatabaseAccessDenied(
        MySqlException exception
    )
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception.ErrorCode == MySqlErrorCode.DatabaseAccessDenied;
    }

    private bool CanConnectToServer()
    {
        try
        {
            using var lease = CreateServerConnection();
            lease.Open();

            return true;
        }
        catch (MySqlException)
        {
            return false;
        }
    }

    private async Task<bool> CanConnectToServerAsync(
        CancellationToken cancellationToken
    )
    {
        try
        {
            await using var lease = CreateServerConnection();
            await lease
                .OpenAsync(cancellationToken)
                .ConfigureAwait(false);

            return true;
        }
        catch (MySqlException)
        {
            return false;
        }
    }
}
