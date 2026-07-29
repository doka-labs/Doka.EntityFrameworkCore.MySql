namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Live integration coverage for the server-side <c>WHERE TABLE_NAME IN (...)</c>
/// scaffolding filter. Creates a 20-table fixture schema, then asserts that
/// scaffolding with an explicit two-table filter returns exactly those two tables
/// (and that scaffolding without a filter returns all twenty). The previous monolith
/// fetched every INFORMATION_SCHEMA row and discarded the rest client-side; the
/// loader hierarchy binds the filter as SQL parameters, so this test pins both the
/// filtered and unfiltered shapes against a real engine.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
public sealed class MySqlScaffoldingFilterTests
{
    private const string TablePrefix = "scaffolding_filter_t";
    private const int TableCount = 20;

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Scaffolding_with_filter_returns_only_requested_tables()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);

        await PrepareSchemaAsync(connectionString)
            .ConfigureAwait(false);

        try
        {
            await using var connection = new MySqlConnection(connectionString);
            await connection
                .OpenAsync()
                .ConfigureAwait(false);

            var factory = new MySqlDatabaseModelFactory(
                new MySqlConnectorDriverFacade(),
                new MySqlScaffoldingContext());

            var filteredModel = factory.Create(
                connection,
                new DatabaseModelFactoryOptions(
                    [
                        TablePrefix + "03",
                        TablePrefix + "07",
                    ],
                    Array.Empty<string>()));

            Assert.Equal(2, filteredModel.Tables.Count);
            Assert.Contains(filteredModel.Tables, table => table.Name == TablePrefix + "03");
            Assert.Contains(filteredModel.Tables, table => table.Name == TablePrefix + "07");
        }
        finally
        {
            await TearDownSchemaAsync(connectionString)
                .ConfigureAwait(false);
        }
    }

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Scaffolding_without_filter_returns_every_table()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);

        await PrepareSchemaAsync(connectionString)
            .ConfigureAwait(false);

        try
        {
            await using var connection = new MySqlConnection(connectionString);
            await connection
                .OpenAsync()
                .ConfigureAwait(false);

            var factory = new MySqlDatabaseModelFactory(
                new MySqlConnectorDriverFacade(),
                new MySqlScaffoldingContext());

            var fullModel = factory.Create(
                connection,
                new DatabaseModelFactoryOptions(Array.Empty<string>(), Array.Empty<string>()));

            // Filter set is empty -> MatchAll. Expect at least our 20 fixture tables;
            // other test suites may run concurrently and leave residual tables behind.
            var fixtureTables = fullModel
                .Tables.Where(table => table.Name.StartsWith(TablePrefix, StringComparison.Ordinal))
                .ToArray();

            Assert.Equal(TableCount, fixtureTables.Length);
        }
        finally
        {
            await TearDownSchemaAsync(connectionString)
                .ConfigureAwait(false);
        }
    }

    private static async Task PrepareSchemaAsync(
        string connectionString
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);

        for (var index = 0; index < TableCount; index++)
        {
            await using var command = connection.CreateCommand();
            var tableName = TablePrefix + index.ToString("D2", CultureInfo.InvariantCulture);
            command.CommandText = $"DROP TABLE IF EXISTS `{tableName}`;"
                + $"CREATE TABLE `{tableName}` ("
                + "  `Id` INT NOT NULL,"
                + "  `Name` VARCHAR(64) NOT NULL,"
                + "  PRIMARY KEY (`Id`)"
                + ") CHARACTER SET utf8mb4;";
            await command
                .ExecuteNonQueryAsync()
                .ConfigureAwait(false);
        }
    }

    private static async Task TearDownSchemaAsync(
        string connectionString
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);

        for (var index = 0; index < TableCount; index++)
        {
            await using var command = connection.CreateCommand();
            var tableName = TablePrefix + index.ToString("D2", CultureInfo.InvariantCulture);
            command.CommandText = $"DROP TABLE IF EXISTS `{tableName}`;";
            await command
                .ExecuteNonQueryAsync()
                .ConfigureAwait(false);
        }
    }
}
