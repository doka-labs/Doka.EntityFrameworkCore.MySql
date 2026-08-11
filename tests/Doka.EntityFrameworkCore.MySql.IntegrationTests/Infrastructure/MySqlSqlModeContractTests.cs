using System.Text;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Executes provider-generated text literals under the SQL modes whose string
/// parsing rules differ across supported MySQL and MariaDB engines.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
[Trait("Category", "ConfigurationContract")]
[Trait("VerificationLane", "FullIntegration")]
public sealed class MySqlSqlModeContractTests
{
    private static readonly string[] s_sqlModes =
    [
        string.Empty,
        "NO_BACKSLASH_ESCAPES",
        "ANSI_QUOTES",
        "ANSI_QUOTES,NO_BACKSLASH_ESCAPES,STRICT_TRANS_TABLES",
    ];

    /// <summary>
    /// Verifies every configured SQL mode against MySQL 8.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task MySql84_preserves_text_literals_across_supported_sql_modes()
    {
        await AssertTextLiteralContractAsync(
                IntegrationDatabaseTarget.MySql84,
                MySqlServerVersion.MySql(new Version(8, 4, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies every configured SQL mode against MySQL 9.7.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public async Task MySql97_preserves_text_literals_across_supported_sql_modes()
    {
        await AssertTextLiteralContractAsync(
                IntegrationDatabaseTarget.MySql97,
                MySqlServerVersion.MySql(new Version(9, 7, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies every configured SQL mode against MariaDB 10.11.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public async Task MariaDb1011_preserves_text_literals_across_supported_sql_modes()
    {
        await AssertTextLiteralContractAsync(
                IntegrationDatabaseTarget.MariaDb1011,
                MySqlServerVersion.MariaDb(new Version(10, 11, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies every configured SQL mode against MariaDB 11.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public async Task MariaDb114_preserves_text_literals_across_supported_sql_modes()
    {
        await AssertTextLiteralContractAsync(
                IntegrationDatabaseTarget.MariaDb114,
                MySqlServerVersion.MariaDb(new Version(11, 4, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies every configured SQL mode against MariaDB 11.8.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_preserves_text_literals_across_supported_sql_modes()
    {
        await AssertTextLiteralContractAsync(
                IntegrationDatabaseTarget.MariaDb118,
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies every configured SQL mode against MariaDB 12.3.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public async Task MariaDb123_preserves_text_literals_across_supported_sql_modes()
    {
        await AssertTextLiteralContractAsync(
                IntegrationDatabaseTarget.MariaDb123,
                MySqlServerVersion.MariaDb(new Version(12, 3, 0)))
            .ConfigureAwait(false);
    }

    private static async Task AssertTextLiteralContractAsync(
        IntegrationDatabaseTarget target,
        MySqlServerVersion serverVersion
    )
    {
        const string payload = "path\\segment 'quoted'\nemoji:\U0001F680";
        var connectionString =
            new MySqlConnectionStringBuilder(IntegrationTestEnvironment.GetConnectionString(target))
            {
                Pooling = false,
            }.ConnectionString;

        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);

        await using var context = new SqlModeContractContext(
            IntegrationTestDbContextOptions.Create<SqlModeContractContext>().UseMySql(connection, serverVersion)
                .Options);
        var mapping = context
            .GetService<IRelationalTypeMappingSource>()
            .FindMapping(typeof(string));

        Assert.NotNull(mapping);

        var literal = mapping.GenerateSqlLiteral(payload);
        var expectedHex = Convert.ToHexString(Encoding.UTF8.GetBytes(payload));

        foreach (var sqlMode in s_sqlModes)
        {
            await SetSqlModeAsync(connection, sqlMode)
                .ConfigureAwait(false);
            var expectedSqlMode = await ReadSqlModeAsync(connection)
                .ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT HEX({literal})";

            var actualHex = Assert.IsType<string>(
                await command
                    .ExecuteScalarAsync()
                    .ConfigureAwait(false));

            Assert.Equal(expectedHex, actualHex);

            await AssertJsonPathLiteralContractAsync(connection, context)
                .ConfigureAwait(false);

            await AssertMigrationLiteralContractAsync(connection, context)
                .ConfigureAwait(false);

            Assert.Equal(
                expectedSqlMode,
                await ReadSqlModeAsync(connection)
                    .ConfigureAwait(false));
        }
    }

    private static async Task AssertJsonPathLiteralContractAsync(
        MySqlConnection connection,
        DbContext context
    )
    {
        const string json = """{"has\\back":"backslash","has\"quote":"quote","apo'stroph":"apostrophe"}""";
        (string PropertyName, string Expected)[] cases =
        [
            ("has\\back", "backslash"),
            ("has\"quote", "quote"),
            ("apo'stroph", "apostrophe"),
        ];
        var queryGenerator = context
            .GetService<IQuerySqlGeneratorFactory>()
            .Create();
        var escapeMethod = queryGenerator
            .GetType()
            .GetMethod(
                "EscapeJsonPathPropertyName",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(escapeMethod);

        foreach (var @case in cases)
        {
            var escapedSegment = Assert.IsType<string>(escapeMethod.Invoke(null, [@case.PropertyName]));
            var jsonLiteral = MySqlSqlLiteralGenerator.Generate(json);
            var pathLiteral = MySqlSqlLiteralGenerator.Generate($"$.{escapedSegment}");
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT JSON_UNQUOTE(JSON_EXTRACT({jsonLiteral}, {pathLiteral}));";

            Assert.Equal(
                @case.Expected,
                Assert.IsType<string>(
                    await command
                        .ExecuteScalarAsync()
                        .ConfigureAwait(false)));
        }
    }

    private static async Task AssertMigrationLiteralContractAsync(
        MySqlConnection connection,
        DbContext context
    )
    {
        const string tableComment = "table\\comment 'quoted'";
        const string columnComment = "column\\comment 'quoted'";
        const string alteredTableComment = "altered\\table 'quoted'";
        const string addedColumnComment = "added\\column 'quoted'";
        const string alteredColumnComment = "altered\\column 'quoted'";
        const string insertedValue = "inserted\\value 'quoted'";
        const string updatedValue = "updated\\value 'quoted'";
        var tableName = $"SqlModeLiteral_{Guid.NewGuid():N}";
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var createTable = new CreateTableOperation
        {
            Name = tableName,
            Comment = tableComment,
        };
        createTable.Columns.Add(
            new AddColumnOperation
            {
                Table = tableName,
                Name = "Value",
                ClrType = typeof(string),
                ColumnType = "longtext",
                IsNullable = false,
                Comment = columnComment,
            });

        try
        {
            await ExecuteOperationsAsync(
                    generator,
                    context.Model,
                    connection,
                    [
                        createTable,
                        new InsertDataOperation
                        {
                            Table = tableName,
                            Columns = ["Value"],
                            ColumnTypes = ["longtext"],
                            Values = new object[,]
                            {
                                { insertedValue },
                            },
                        },
                    ])
                .ConfigureAwait(false);

            Assert.Equal(
                tableComment,
                await ReadTableCommentAsync(connection, tableName)
                    .ConfigureAwait(false));
            Assert.Equal(
                columnComment,
                await ReadColumnCommentAsync(connection, tableName, "Value")
                    .ConfigureAwait(false));
            Assert.Equal(
                Convert.ToHexString(Encoding.UTF8.GetBytes(insertedValue)),
                await ReadStoredValueHexAsync(connection, tableName)
                    .ConfigureAwait(false));

            var alterTable = new AlterTableOperation
            {
                Name = tableName,
                Comment = alteredTableComment,
            };
            alterTable.OldTable.Comment = tableComment;
            var addColumn = new AddColumnOperation
            {
                Table = tableName,
                Name = "AdditionalValue",
                ClrType = typeof(string),
                ColumnType = "longtext",
                IsNullable = true,
                Comment = addedColumnComment,
            };

            await ExecuteOperationsAsync(
                    generator,
                    context.Model,
                    connection,
                    [
                        alterTable,
                        addColumn
                    ])
                .ConfigureAwait(false);

            Assert.Equal(
                alteredTableComment,
                await ReadTableCommentAsync(connection, tableName)
                    .ConfigureAwait(false));
            Assert.Equal(
                addedColumnComment,
                await ReadColumnCommentAsync(connection, tableName, addColumn.Name)
                    .ConfigureAwait(false));

            var alterColumn = new AlterColumnOperation
            {
                Table = tableName,
                Name = addColumn.Name,
                ClrType = typeof(string),
                ColumnType = "longtext",
                IsNullable = true,
                Comment = alteredColumnComment,
            };
            alterColumn.OldColumn.ClrType = typeof(string);
            alterColumn.OldColumn.ColumnType = "longtext";
            alterColumn.OldColumn.IsNullable = true;
            alterColumn.OldColumn.Comment = addedColumnComment;

            await ExecuteOperationsAsync(generator, context.Model, connection, [alterColumn])
                .ConfigureAwait(false);

            Assert.Equal(
                alteredColumnComment,
                await ReadColumnCommentAsync(connection, tableName, addColumn.Name)
                    .ConfigureAwait(false));

            await ExecuteOperationsAsync(
                    generator,
                    context.Model,
                    connection,
                    [
                        new UpdateDataOperation
                        {
                            Table = tableName,
                            Columns = ["Value"],
                            ColumnTypes = ["longtext"],
                            Values = new object[,]
                            {
                                { updatedValue },
                            },
                            KeyColumns = ["Value"],
                            KeyColumnTypes = ["longtext"],
                            KeyValues = new object[,]
                            {
                                { insertedValue },
                            },
                        },
                    ])
                .ConfigureAwait(false);

            Assert.Equal(
                Convert.ToHexString(Encoding.UTF8.GetBytes(updatedValue)),
                await ReadStoredValueHexAsync(connection, tableName)
                    .ConfigureAwait(false));

            await ExecuteOperationsAsync(
                    generator,
                    context.Model,
                    connection,
                    [
                        new DeleteDataOperation
                        {
                            Table = tableName,
                            KeyColumns = ["Value"],
                            KeyColumnTypes = ["longtext"],
                            KeyValues = new object[,]
                            {
                                { updatedValue },
                            },
                        },
                    ])
                .ConfigureAwait(false);

            await using var countCommand = connection.CreateCommand();
            countCommand.CommandText = $"SELECT COUNT(*) FROM `{tableName}`;";

            Assert.Equal(
                0L,
                Convert.ToInt64(
                    await countCommand
                        .ExecuteScalarAsync()
                        .ConfigureAwait(false),
                    CultureInfo.InvariantCulture));
        }
        finally
        {
            await using var dropCommand = connection.CreateCommand();
            dropCommand.CommandText = $"DROP TABLE IF EXISTS `{tableName}`;";
            _ = await dropCommand
                .ExecuteNonQueryAsync()
                .ConfigureAwait(false);
        }
    }

    private static async Task ExecuteOperationsAsync(
        IMigrationsSqlGenerator generator,
        IModel model,
        MySqlConnection connection,
        IReadOnlyList<MigrationOperation> operations
    )
    {
        foreach (var migrationCommand in generator.Generate(operations, model))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = migrationCommand.CommandText;
            _ = await command
                .ExecuteNonQueryAsync()
                .ConfigureAwait(false);
        }
    }

    private static async Task<string> ReadTableCommentAsync(
        MySqlConnection connection,
        string tableName
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TABLE_COMMENT FROM information_schema.TABLES "
            + "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @tableName;";
        command.Parameters.AddWithValue("@tableName", tableName);

        return Assert.IsType<string>(
            await command
                .ExecuteScalarAsync()
                .ConfigureAwait(false));
    }

    private static async Task<string> ReadColumnCommentAsync(
        MySqlConnection connection,
        string tableName,
        string columnName
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COLUMN_COMMENT FROM information_schema.COLUMNS "
            + "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @tableName AND COLUMN_NAME = @columnName;";
        command.Parameters.AddWithValue("@tableName", tableName);
        command.Parameters.AddWithValue("@columnName", columnName);

        return Assert.IsType<string>(
            await command
                .ExecuteScalarAsync()
                .ConfigureAwait(false));
    }

    private static async Task<string> ReadStoredValueHexAsync(
        MySqlConnection connection,
        string tableName
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT HEX(`Value`) FROM `{tableName}`;";

        return Assert.IsType<string>(
            await command
                .ExecuteScalarAsync()
                .ConfigureAwait(false));
    }

    private static async Task SetSqlModeAsync(
        MySqlConnection connection,
        string sqlMode
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SET SESSION sql_mode = @sqlMode";
        command.Parameters.AddWithValue("@sqlMode", sqlMode);

        _ = await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    private static async Task<string> ReadSqlModeAsync(
        MySqlConnection connection
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT @@SESSION.sql_mode;";

        return Assert.IsType<string>(
            await command
                .ExecuteScalarAsync()
                .ConfigureAwait(false));
    }

    private sealed class SqlModeContractContext : DbContext
    {
        public SqlModeContractContext(
            DbContextOptions<SqlModeContractContext> options
        ) : base(options) { }
    }
}
