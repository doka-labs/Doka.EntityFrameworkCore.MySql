namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies SQL-generated Guid defaults through public migrations and live scaffolding.
/// </summary>
[IntegrationDatabaseTest]
[Trait("Category", "MigrationContract")]
[Trait("VerificationLane", "FullIntegration")]
public sealed partial class MySqlGuidSqlDefaultDdlIntegrationTests
{
    private const string BinaryDefaultSql = "UNHEX(REPLACE(UUID(), '-', ''))";

    /// <summary>
    /// Identifies each supported engine and usable SQL-default storage format.
    /// </summary>
    public static IEnumerable<object[]> CreateTableCases =>
        from target in SelectedTargets()
        from format in FormatsFor(target)
        select new object[] { target.ToString(), format };

    /// <summary>
    /// Identifies MariaDB formats whose populated tables accept nondeterministic defaults.
    /// </summary>
    public static IEnumerable<object[]> PopulatedAddColumnCases =>
        CreateTableCases.Where(values => IsMariaDb(Enum.Parse<IntegrationDatabaseTarget>((string)values[0])));

    /// <summary>
    /// Identifies MySQL formats whose ADD COLUMN restriction is measured independently.
    /// </summary>
    public static IEnumerable<object[]> RejectedAddColumnCases =>
        CreateTableCases.Where(values => !IsMariaDb(Enum.Parse<IntegrationDatabaseTarget>((string)values[0])));

    /// <summary>
    /// Identifies each selected supported engine for the invalid Binary16 expression control.
    /// </summary>
    public static IEnumerable<object[]> BinaryDefaultCases =>
        SelectedTargets().Select(target => new object[] { target.ToString() });

    /// <summary>
    /// The literal table.Column&lt;Guid&gt; API retains its SQL expression and generates a fresh ID per omitted value.
    /// </summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(CreateTableCases))]
    public async Task CreateTable_guid_sql_default_generates_distinct_valid_values(
        string target,
        MySqlGuidFormat format
    )
    {
        // Arrange
        var databaseTarget = Enum.Parse<IntegrationDatabaseTarget>(target);
        var tableName = CreateTableName();
        await using var connection = CreateConnection(databaseTarget);
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await using var context = CreateContext(connection, databaseTarget, format);
        var migrationBuilder = CreateGuidDefaultTable(tableName, format, DefaultSqlFor(format));

        try
        {
            // Act
            var sql = await ExecuteOperationsAsync(context, connection, migrationBuilder.Operations)
                .ConfigureAwait(false);
            await ExecuteRawAsync(connection, $"INSERT INTO `{tableName}` (`Value`) VALUES (1), (2);")
                .ConfigureAwait(false);
            var rows = await ReadRowsAsync(connection, tableName, format).ConfigureAwait(false);

            // Assert
            Assert.Contains($"DEFAULT ({DefaultSqlFor(format)})", sql, StringComparison.OrdinalIgnoreCase);
            AssertGeneratedRows(rows, format, [1, 2]);
        }
        finally
        {
            await DropTableAsync(connection, tableName).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// MariaDB backfills each existing row and retains the Guid SQL default for subsequent omitted values.
    /// </summary>
    [RequiresDatabaseTargetsTheory(
        IntegrationDatabaseTarget.MariaDb1011,
        IntegrationDatabaseTarget.MariaDb114,
        IntegrationDatabaseTarget.MariaDb118,
        IntegrationDatabaseTarget.MariaDb123)]
    [MemberData(nameof(PopulatedAddColumnCases))]
    public async Task AddColumn_guid_sql_default_backfills_populated_table_and_generates_new_values(
        string target,
        MySqlGuidFormat format
    )
    {
        // Arrange
        var databaseTarget = Enum.Parse<IntegrationDatabaseTarget>(target);
        var tableName = CreateTableName();
        await using var connection = CreateConnection(databaseTarget);
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await using var context = CreateContext(connection, databaseTarget, format);

        try
        {
            await CreatePopulatedTableAsync(connection, tableName).ConfigureAwait(false);
            var migrationBuilder = CreateAddGuidDefaultColumn(tableName, format);

            // Act
            var sql = await ExecuteOperationsAsync(context, connection, migrationBuilder.Operations)
                .ConfigureAwait(false);
            var backfilledRows = await ReadRowsAsync(connection, tableName, format).ConfigureAwait(false);
            await ExecuteRawAsync(connection, $"INSERT INTO `{tableName}` (`Value`) VALUES (3), (4);")
                .ConfigureAwait(false);
            var allRows = await ReadRowsAsync(connection, tableName, format).ConfigureAwait(false);

            // Assert
            Assert.Contains($"DEFAULT ({DefaultSqlFor(format)})", sql, StringComparison.OrdinalIgnoreCase);
            AssertGeneratedRows(backfilledRows, format, [1, 2]);
            AssertGeneratedRows(allRows, format, [1, 2, 3, 4]);
            Assert.Equal(backfilledRows, allRows.Take(2).ToArray());
        }
        finally
        {
            await DropTableAsync(connection, tableName).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// MySQL rejects nondeterministic ADD COLUMN defaults with error 1674 while preserving the populated table.
    /// </summary>
    [RequiresDatabaseTargetsTheory(IntegrationDatabaseTarget.MySql84, IntegrationDatabaseTarget.MySql97)]
    [MemberData(nameof(RejectedAddColumnCases))]
    public async Task AddColumn_nondeterministic_guid_default_is_rejected_without_schema_or_data_changes(
        string target,
        MySqlGuidFormat format
    )
    {
        // Arrange
        var databaseTarget = Enum.Parse<IntegrationDatabaseTarget>(target);
        var tableName = CreateTableName();
        await using var connection = CreateConnection(databaseTarget);
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await using var context = CreateContext(connection, databaseTarget, format);

        try
        {
            await CreatePopulatedTableAsync(connection, tableName).ConfigureAwait(false);
            var definitionBefore = await ReadTableDefinitionAsync(connection, tableName).ConfigureAwait(false);
            var migrationBuilder = CreateAddGuidDefaultColumn(tableName, format);

            // Act
            // WHY: This is an engine restriction; changing binary logging would remove the tested contract.
            var exception = await Assert.ThrowsAsync<MySqlException>(
                () => ExecuteOperationsAsync(context, connection, migrationBuilder.Operations));
            var definitionAfter = await ReadTableDefinitionAsync(connection, tableName).ConfigureAwait(false);
            var values = await ReadValuesAsync(connection, tableName).ConfigureAwait(false);

            // Assert
            Assert.Equal(1674, exception.Number);
            Assert.Equal(definitionBefore, definitionAfter);
            Assert.Equal([1, 2], values);
        }
        finally
        {
            await DropTableAsync(connection, tableName).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A textual UUID expression is not silently converted to binary storage and fails without inserting a row.
    /// </summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(BinaryDefaultCases))]
    public async Task Binary16_textual_uuid_default_rejects_omitted_id_with_error_1406(
        string target
    )
    {
        // Arrange
        var databaseTarget = Enum.Parse<IntegrationDatabaseTarget>(target);
        var tableName = CreateTableName();
        await using var connection = CreateConnection(databaseTarget);
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await using var context = CreateContext(connection, databaseTarget, MySqlGuidFormat.Binary16);
        var migrationBuilder = CreateGuidDefaultTable(tableName, MySqlGuidFormat.Binary16, "UUID()");

        try
        {
            _ = await ExecuteOperationsAsync(context, connection, migrationBuilder.Operations).ConfigureAwait(false);

            // Act
            var exception = await Assert.ThrowsAsync<MySqlException>(
                () => ExecuteRawAsync(connection, $"INSERT INTO `{tableName}` (`Value`) VALUES (1);"));
            var values = await ReadValuesAsync(connection, tableName).ConfigureAwait(false);

            // Assert
            Assert.Equal(1406, exception.Number);
            Assert.Empty(values);
        }
        finally
        {
            await DropTableAsync(connection, tableName).ConfigureAwait(false);
        }
    }

    // WHY: All topic partials must use the same CallerFilePath-owned integration schema.
    private static string GetConnectionString(
        IntegrationDatabaseTarget target
    ) => IntegrationTestEnvironment.GetConnectionString(target);

    private static IEnumerable<IntegrationDatabaseTarget> SelectedTargets() =>
        IntegrationTestEnvironment.GetSupportedTargets().Where(IntegrationTestEnvironment.IsTargetSelected);

    private static bool IsMariaDb(
        IntegrationDatabaseTarget target
    ) => IntegrationTestEnvironment.GetServerVersion(target).IsMariaDb;

    private static IEnumerable<MySqlGuidFormat> FormatsFor(
        IntegrationDatabaseTarget target
    ) => IsMariaDb(target)
        ? [MySqlGuidFormat.Char36, MySqlGuidFormat.Binary16, MySqlGuidFormat.Uuid]
        : [MySqlGuidFormat.Char36, MySqlGuidFormat.Binary16];
}
