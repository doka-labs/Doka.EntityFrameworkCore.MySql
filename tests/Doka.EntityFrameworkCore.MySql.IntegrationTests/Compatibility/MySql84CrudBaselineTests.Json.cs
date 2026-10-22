namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySql84CrudBaselineTests
{
    /// <summary>
    /// Verifies that the provider-generated MySQL 8.4 JSON and generated-column DDL executes successfully.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task MySql84_json_and_generated_column_baseline_succeeds()
    {
        // Arrange
        var connectionString = GetConnectionString();

        await ResetDatabaseObjectsAsync(connectionString)
            .ConfigureAwait(false);

        try
        {
            await using var context = new MySql84CrudContext(CreateOptions(connectionString));
            var migrationsSqlGenerator = context.GetService<IMigrationsSqlGenerator>();
            // Act
            var commands = migrationsSqlGenerator.Generate(
                [
                    new CreateTableOperation
                    {
                        Name = JsonTableName,
                        Columns =
                        {
                            new AddColumnOperation
                            {
                                Name = "Id",
                                ClrType = typeof(int),
                                ColumnType = "int",
                                IsNullable = false,
                            },
                            new AddColumnOperation
                            {
                                Name = "Payload",
                                ClrType = typeof(string),
                                ColumnType = "json",
                                IsNullable = false,
                            },
                            new AddColumnOperation
                            {
                                Name = "StoredCount",
                                ClrType = typeof(int),
                                ColumnType = "int",
                                ComputedColumnSql = "JSON_LENGTH(`Payload`)",
                                IsStored = true,
                                IsNullable = false,
                            },
                        },
                        PrimaryKey = new AddPrimaryKeyOperation
                        {
                            Name = $"PK_{JsonTableName}",
                            Table = JsonTableName,
                            Columns = ["Id"],
                        },
                    },
                ],
                context.Model);

            await using var dbConnection = context.Database.GetDbConnection();

            if (dbConnection.State != ConnectionState.Open)
            {
                await dbConnection
                    .OpenAsync()
                    .ConfigureAwait(false);
            }

            foreach (var command in commands)
            {
                await using var ddlCommand = dbConnection.CreateCommand();
                ddlCommand.CommandText = command.CommandText;
                await ddlCommand
                    .ExecuteNonQueryAsync()
                    .ConfigureAwait(false);
            }

            await using var insertCommand = dbConnection.CreateCommand();
            insertCommand.CommandText = $"INSERT INTO `{JsonTableName}` (`Id`, `Payload`) VALUES (@id, @payload);";

            var idParameter = insertCommand.CreateParameter();
            idParameter.ParameterName = "@id";
            idParameter.Value = 1;
            insertCommand.Parameters.Add(idParameter);

            var payloadParameter = insertCommand.CreateParameter();
            payloadParameter.ParameterName = "@payload";
            payloadParameter.Value = """{"kind":"alpha","items":[1,2,3]}""";
            insertCommand.Parameters.Add(payloadParameter);

            await insertCommand
                .ExecuteNonQueryAsync()
                .ConfigureAwait(false);

            await using var scalarCommand = dbConnection.CreateCommand();
            scalarCommand.CommandText = $"SELECT `StoredCount` FROM `{JsonTableName}` WHERE `Id` = 1;";

            var result = await scalarCommand
                .ExecuteScalarAsync()
                .ConfigureAwait(false);

            var tableExists = await TableExistsAsync(connectionString, JsonTableName)
                .ConfigureAwait(false);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, Convert.ToInt32(result, CultureInfo.InvariantCulture));
            Assert.True(tableExists);
        }
        finally
        {
            await ResetDatabaseObjectsAsync(connectionString)
                .ConfigureAwait(false);
        }
    }
}
