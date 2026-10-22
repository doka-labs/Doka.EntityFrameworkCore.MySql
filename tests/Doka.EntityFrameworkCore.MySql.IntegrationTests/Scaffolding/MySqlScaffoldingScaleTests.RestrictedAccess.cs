namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Shares the restrictedaccess responsibility without changing the test fixture.</summary>
public sealed partial class MySqlScaffoldingScaleTests
{
    private static async Task<int> VerifyRestrictedMetadataAsync(
        string rootConnectionString,
        string[] tableNames,
        int visibleTableCount,
        int maximumSetBasedMetadataCommands,
        IntegrationDatabaseTarget target
    )
    {
        var account = RestrictedAccount.Create();
        var executedCommandCount = 0;

        try
        {
            await CreateRestrictedAccountAsync(
                    rootConnectionString,
                    account,
                    tableNames.Take(visibleTableCount))
                .ConfigureAwait(false);

            var restrictedConnectionString = CreateRestrictedConnectionString(
                rootConnectionString,
                account);

            await AssertTableAccessDeniedAsync(
                    restrictedConnectionString,
                    tableNames[visibleTableCount])
                .ConfigureAwait(false);

            await using var connection = new CountingDbConnection(
                new MySqlConnection(restrictedConnectionString));

            await connection
                .OpenAsync()
                .ConfigureAwait(false);

            var factory = new MySqlDatabaseModelFactory(
                new MySqlConnectorDriverFacade(),
                new MySqlScaffoldingContext());

            var databaseModel = factory.Create(
                connection,
                new DatabaseModelFactoryOptions(Array.Empty<string>(), Array.Empty<string>()));

            var expectedVisibleTables = tableNames
                .Take(visibleTableCount)
                .OrderBy(tableName => tableName, StringComparer.Ordinal)
                .ToArray();

            var actualVisibleTables = databaseModel
                .Tables.Select(table => table.Name)
                .OrderBy(tableName => tableName, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(expectedVisibleTables, actualVisibleTables);
            AssertMetadataCommandContract(
                new MetadataCommandMetrics(
                    connection.ExecutedCommandCount,
                    connection.ExecutedCommandTexts),
                target,
                visibleTableCount,
                maximumSetBasedMetadataCommands);

            Assert.Equal(ConnectionState.Open, connection.State);
            executedCommandCount = connection.ExecutedCommandCount;
        }
        finally
        {
            await DropRestrictedAccountAsync(rootConnectionString, account)
                .ConfigureAwait(false);
            await AssertRestrictedAccountRemovedAsync(rootConnectionString, account)
                .ConfigureAwait(false);
        }

        return executedCommandCount;
    }

    private static async Task CreateRestrictedAccountAsync(
        string rootConnectionString,
        RestrictedAccount account,
        IEnumerable<string> visibleTableNames
    )
    {
        await using var connection = new MySqlConnection(rootConnectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);

        var databaseName = connection.Database;

        await using (var createUser = connection.CreateCommand())
        {
            createUser.CommandText =
                $"CREATE USER '{account.UserName}'@'%' IDENTIFIED BY '{account.Password}';";
            await createUser
                .ExecuteNonQueryAsync()
                .ConfigureAwait(false);
        }

        foreach (var tableName in visibleTableNames)
        {
            await using var grant = connection.CreateCommand();
            grant.CommandText =
                $"GRANT SELECT ON {DelimitIdentifier(databaseName)}.{DelimitIdentifier(tableName)} "
                + $"TO '{account.UserName}'@'%';";
            await grant
                .ExecuteNonQueryAsync()
                .ConfigureAwait(false);
        }
    }

    private static async Task DropRestrictedAccountAsync(
        string rootConnectionString,
        RestrictedAccount account
    )
    {
        await using var connection = new MySqlConnection(rootConnectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = $"DROP USER IF EXISTS '{account.UserName}'@'%';";
        await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    private static async Task AssertRestrictedAccountRemovedAsync(
        string rootConnectionString,
        RestrictedAccount account
    )
    {
        // Arrange
        var restrictedConnectionString = CreateRestrictedConnectionString(
            rootConnectionString,
            account);

        await using var connection = new MySqlConnection(restrictedConnectionString);

        // Act
        var exception = await Assert.ThrowsAsync<MySqlException>(connection.OpenAsync)
            .ConfigureAwait(false);

        // Assert
        Assert.Equal(1045, exception.Number);
    }

    private static async Task AssertTableAccessDeniedAsync(
        string restrictedConnectionString,
        string deniedTableName
    )
    {
        // Arrange
        await using var connection = new MySqlConnection(restrictedConnectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = $"SELECT COUNT(*) FROM {DelimitIdentifier(deniedTableName)};";

        // Act
        var exception = await Assert.ThrowsAsync<MySqlException>(command.ExecuteScalarAsync)
            .ConfigureAwait(false);

        // Assert
        Assert.Equal(1142, exception.Number);
    }

    private static string CreateRestrictedConnectionString(
        string rootConnectionString,
        RestrictedAccount account
    )
    {
        var builder = new MySqlConnectionStringBuilder(rootConnectionString)
        {
            UserID = account.UserName,
            Password = account.Password,
            Pooling = false,
        };

        return builder.ConnectionString;
    }
}
