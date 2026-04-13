namespace Doka.EntityFrameworkCore.MySql;

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

    private readonly IMySqlDriverFacade _driverFacade;

    public MySqlRelationalDatabaseCreator(
        RelationalDatabaseCreatorDependencies dependencies,
        IMySqlDriverFacade driverFacade
    ) : base(dependencies)
    {
        _driverFacade = driverFacade ?? throw new ArgumentNullException(nameof(driverFacade));
    }

    public override bool Exists()
    {
        try
        {
            using var connection = CreateDatabaseConnection();
            connection.Open();

            return true;
        }
        catch (MySqlException exception)
        {
            if (IsMissingDatabase(exception))
            {
                return false;
            }

            if (IsMissingDatabaseAccessDenied(exception)
                && CanConnectToServer())
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
            await using var connection = CreateDatabaseConnection();
            await connection
                .OpenAsync(cancellationToken)
                .ConfigureAwait(false);

            return true;
        }
        catch (MySqlException exception)
        {
            if (IsMissingDatabase(exception))
            {
                return false;
            }

            if (IsMissingDatabaseAccessDenied(exception)
                && await CanConnectToServerAsync(cancellationToken)
                    .ConfigureAwait(false))
            {
                return false;
            }

            throw;
        }
    }

    public override void Create()
    {
        using var connection = CreateServerConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            $"CREATE DATABASE IF NOT EXISTS {Dependencies.SqlGenerationHelper.DelimitIdentifier(GetDatabaseName())}{Dependencies.SqlGenerationHelper.StatementTerminator}";
        command.ExecuteNonQuery();
    }

    public override async Task CreateAsync(
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = CreateServerConnection();
        await connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"CREATE DATABASE IF NOT EXISTS {Dependencies.SqlGenerationHelper.DelimitIdentifier(GetDatabaseName())}{Dependencies.SqlGenerationHelper.StatementTerminator}";
        await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public override void Delete()
    {
        using var connection = CreateServerConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            $"DROP DATABASE IF EXISTS {Dependencies.SqlGenerationHelper.DelimitIdentifier(GetDatabaseName())}{Dependencies.SqlGenerationHelper.StatementTerminator}";
        command.ExecuteNonQuery();
    }

    public override async Task DeleteAsync(
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = CreateServerConnection();
        await connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"DROP DATABASE IF EXISTS {Dependencies.SqlGenerationHelper.DelimitIdentifier(GetDatabaseName())}{Dependencies.SqlGenerationHelper.StatementTerminator}";
        await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public override bool HasTables()
    {
        using var connection = CreateDatabaseConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = HasTablesSql;

        var result = command.ExecuteScalar();

        return MySqlScalarConvert.ToBoolean(result);
    }

    public override async Task<bool> HasTablesAsync(
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = CreateDatabaseConnection();
        await connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = HasTablesSql;

        var result = await command
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);

        return MySqlScalarConvert.ToBoolean(result);
    }

    private DbConnection CreateDatabaseConnection() => _driverFacade.CreateConnection(GetConnectionString());

    private DbConnection CreateServerConnection() => _driverFacade.CreateConnection(CreateServerConnectionString());

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

        if (extension?.DataSource is { } dataSource)
        {
            var dataSourceConnectionString = dataSource.ConnectionString;

            if (!string.IsNullOrWhiteSpace(dataSourceConnectionString))
            {
                return dataSourceConnectionString;
            }
        }

        var connectionString = Dependencies.Connection.DbConnection.ConnectionString;

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

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
            using var connection = CreateServerConnection();
            connection.Open();

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
            await using var connection = CreateServerConnection();
            await connection
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
