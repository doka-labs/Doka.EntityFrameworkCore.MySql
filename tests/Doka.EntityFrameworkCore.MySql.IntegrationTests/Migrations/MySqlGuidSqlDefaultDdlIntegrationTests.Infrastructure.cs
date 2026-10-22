namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlGuidSqlDefaultDdlIntegrationTests
{
    private static string CreateTableName() => $"GuidSqlDefaults_{Guid.NewGuid():N}";

    private static string StoreTypeFor(
        MySqlGuidFormat format
    ) => format switch
    {
        MySqlGuidFormat.Char36 => "char(36)",
        MySqlGuidFormat.Binary16 => "binary(16)",
        MySqlGuidFormat.Uuid => "uuid",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported SQL-default format."),
    };

    private static string DefaultSqlFor(
        MySqlGuidFormat format
    ) => format == MySqlGuidFormat.Binary16 ? BinaryDefaultSql : "UUID()";

    private static MySqlConnection CreateConnection(
        IntegrationDatabaseTarget target
    ) => new(new MySqlConnectionStringBuilder(GetConnectionString(target))
    {
        GuidFormat = MySqlConnector.MySqlGuidFormat.Binary16,
        Pooling = false,
    }.ConnectionString);

    private static GuidSqlDefaultDdlContext CreateContext(
        MySqlConnection connection,
        IntegrationDatabaseTarget target,
        MySqlGuidFormat format
    ) => new(IntegrationTestDbContextOptions.Create<GuidSqlDefaultDdlContext>()
        .UseMySql(
            connection,
            IntegrationTestEnvironment.GetServerVersion(target),
            options => options.DefaultGuidFormat(format))
        .Options);

    private static MigrationBuilder CreateGuidDefaultTable(
        string tableName,
        MySqlGuidFormat format,
        string defaultValueSql
    )
    {
        var migrationBuilder = new MigrationBuilder("Doka.EntityFrameworkCore.MySql");

        migrationBuilder.CreateTable(
            tableName,
            columns: table => new
            {
                Id = table.Column<Guid>(type: StoreTypeFor(format), nullable: false, defaultValueSql: defaultValueSql),
                Value = table.Column<int>(type: "int", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_" + tableName, row => row.Id));

        return migrationBuilder;
    }

    private static MigrationBuilder CreateAddGuidDefaultColumn(
        string tableName,
        MySqlGuidFormat format
    )
    {
        var migrationBuilder = new MigrationBuilder("Doka.EntityFrameworkCore.MySql");

        migrationBuilder.AddColumn<Guid>(
            name: "Id",
            table: tableName,
            type: StoreTypeFor(format),
            nullable: false,
            defaultValueSql: DefaultSqlFor(format));

        return migrationBuilder;
    }

    private static Task CreatePopulatedTableAsync(
        MySqlConnection connection,
        string tableName
    ) => ExecuteRawAsync(
        connection,
        $"CREATE TABLE `{tableName}` (`Value` int NOT NULL PRIMARY KEY); "
        + $"INSERT INTO `{tableName}` (`Value`) VALUES (1), (2);");

    private static async Task<string> ExecuteOperationsAsync(
        DbContext context,
        MySqlConnection connection,
        IReadOnlyList<MigrationOperation> operations
    )
    {
        // WHY: A literal MigrationBuilder operation must work without a model supplying missing column facets.
        var commands = context.GetService<IMigrationsSqlGenerator>().Generate(operations);

        foreach (var migrationCommand in commands)
        {
            await ExecuteRawAsync(connection, migrationCommand.CommandText).ConfigureAwait(false);
        }

        return string.Join(Environment.NewLine, commands.Select(command => command.CommandText));
    }

    private static async Task ExecuteRawAsync(
        MySqlConnection connection,
        string sql
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        _ = await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static Task DropTableAsync(
        MySqlConnection connection,
        string tableName
    ) => ExecuteRawAsync(connection, $"DROP TABLE IF EXISTS `{tableName}`;");

    private static async Task<GuidDefaultRow[]> ReadRowsAsync(
        MySqlConnection connection,
        string tableName,
        MySqlGuidFormat format
    )
    {
        var idExpression = format == MySqlGuidFormat.Binary16 ? "HEX(`Id`)" : "CAST(`Id` AS CHAR(36))";
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT `Value`, {idExpression}, OCTET_LENGTH(`Id`) "
            + $"FROM `{tableName}` ORDER BY `Value`;";
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(false);
        var rows = new List<GuidDefaultRow>();

        while (await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false))
        {
            rows.Add(new GuidDefaultRow(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2)));
        }

        return rows.ToArray();
    }

    private static async Task<int[]> ReadValuesAsync(
        MySqlConnection connection,
        string tableName
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT `Value` FROM `{tableName}` ORDER BY `Value`;";
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(false);
        var values = new List<int>();

        while (await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false))
        {
            values.Add(reader.GetInt32(0));
        }

        return values.ToArray();
    }

    private static async Task<string> ReadTableDefinitionAsync(
        MySqlConnection connection,
        string tableName
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SHOW CREATE TABLE `{tableName}`;";
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(false);
        _ = await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false);

        return reader.GetString(1);
    }

    private static void AssertGeneratedRows(
        GuidDefaultRow[] rows,
        MySqlGuidFormat format,
        int[] expectedValues
    )
    {
        Assert.Equal(expectedValues, rows.Select(row => row.Value).ToArray());
        Assert.Equal(rows.Length, rows.Select(row => row.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(rows, row =>
        {
            Assert.True(Guid.TryParseExact(row.Id, format == MySqlGuidFormat.Binary16 ? "N" : "D", out var id));
            Assert.NotEqual(Guid.Empty, id);

            if (format == MySqlGuidFormat.Binary16)
            {
                Assert.Equal(16, row.ByteLength);
            }
        });
    }

    private sealed record GuidDefaultRow(int Value, string Id, int ByteLength);

    private sealed class GuidSqlDefaultDdlContext : DbContext
    {
        public GuidSqlDefaultDdlContext(
            DbContextOptions<GuidSqlDefaultDdlContext> options
        ) : base(options) { }
    }
}
