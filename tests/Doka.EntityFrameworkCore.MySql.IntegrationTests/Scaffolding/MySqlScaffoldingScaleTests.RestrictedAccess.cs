namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Shares the restrictedaccess responsibility without changing the test fixture.</summary>
public sealed partial class MySqlScaffoldingScaleTests
{
    /// <summary>
    /// Verifies restricted metadata for the integration contract.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel setup and execution.</param>
    /// <param name="rootConnectionString">The root connection string.</param>
    /// <param name="tableNames">The table names.</param>
    /// <param name="visibleTableCount">The visible table count.</param>
    /// <param name="maximumSetBasedMetadataCommands">The maximum set based metadata commands.</param>
    /// <param name="target">The database target used by the test.</param>
    private static async Task<int> VerifyRestrictedMetadataAsync(
        string rootConnectionString,
        string[] tableNames,
        int visibleTableCount,
        int maximumSetBasedMetadataCommands,
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken
    )
    {
        var account = RestrictedAccount.Create();
        var executedCommandCount = 0;

        try
        {
            await CreateRestrictedAccountAsync(
                    rootConnectionString,
                    account,
                    tableNames.Take(visibleTableCount),
                    cancellationToken)
                .ConfigureAwait(false);

            var restrictedConnectionString = CreateRestrictedConnectionString(
                rootConnectionString,
                account);

            await AssertTableAccessDeniedAsync(
                    restrictedConnectionString,
                    tableNames[visibleTableCount],
                    cancellationToken)
                .ConfigureAwait(false);

            await using var connection = new CountingDbConnection(
                new MySqlConnection(restrictedConnectionString));

            await connection
                .OpenAsync(cancellationToken)
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
                .Tables
                .Select(table => table.Name)
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

            await AssertRestrictedAccountRemovedAsync(rootConnectionString, account, CancellationToken.None)
                .ConfigureAwait(false);
        }

        return executedCommandCount;
    }

    /// <summary>
    /// Creates restricted account for the integration contract.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel setup and execution.</param>
    /// <param name="rootConnectionString">The root connection string.</param>
    /// <param name="account">The account.</param>
    /// <param name="visibleTableNames">The visible table names.</param>
    private static async Task CreateRestrictedAccountAsync(
        string rootConnectionString,
        RestrictedAccount account,
        IEnumerable<string> visibleTableNames,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new MySqlConnection(rootConnectionString);
        await connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        var databaseName = connection.Database;

        await using (var createUser = connection.CreateCommand())
        {
            createUser.CommandText =
                $"CREATE USER '{account.UserName}'@'%' IDENTIFIED BY '{account.Password}';";

            await createUser
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var tableName in visibleTableNames)
        {
            await using var grant = connection.CreateCommand();
            grant.CommandText =
                $"GRANT SELECT ON {DelimitIdentifier(databaseName)}.{DelimitIdentifier(tableName)} "
                + $"TO '{account.UserName}'@'%';";

            await grant
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    // WHY: Cleanup must finish after caller cancellation to leave no test resources behind.
    private static async Task DropRestrictedAccountAsync(
        string rootConnectionString,
        RestrictedAccount account
    )
    {
        await using var connection = new MySqlConnection(rootConnectionString);
        await connection
            .OpenAsync(CancellationToken.None)
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();

        command.CommandText = $"DROP USER IF EXISTS '{account.UserName}'@'%';";
        await command
            .ExecuteNonQueryAsync(CancellationToken.None)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies restricted account removed for the integration contract.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel setup and execution.</param>
    /// <param name="rootConnectionString">The root connection string.</param>
    /// <param name="account">The account.</param>
    private static async Task AssertRestrictedAccountRemovedAsync(
        string rootConnectionString,
        RestrictedAccount account,
        CancellationToken cancellationToken
    )
    {
        // Arrange
        var restrictedConnectionString = CreateRestrictedConnectionString(
            rootConnectionString,
            account);

        await using var connection = new MySqlConnection(restrictedConnectionString);

        // Act
        var exception = await Assert
            .ThrowsAsync<MySqlException>(() => connection.OpenAsync(cancellationToken))
            .ConfigureAwait(false);

        // Assert
        Assert.Equal(1045, exception.Number);
    }

    /// <summary>
    /// Verifies table access denied for the integration contract.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel setup and execution.</param>
    /// <param name="restrictedConnectionString">The restricted connection string.</param>
    /// <param name="deniedTableName">The denied table name.</param>
    private static async Task AssertTableAccessDeniedAsync(
        string restrictedConnectionString,
        string deniedTableName,
        CancellationToken cancellationToken
    )
    {
        // Arrange
        await using var connection = new MySqlConnection(restrictedConnectionString);
        await connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();

        command.CommandText = $"SELECT COUNT(*) FROM {DelimitIdentifier(deniedTableName)};";

        // Act
        var exception = await Assert
            .ThrowsAsync<MySqlException>(() => command.ExecuteScalarAsync(cancellationToken))
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
