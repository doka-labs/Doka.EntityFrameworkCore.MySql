namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlGuidMigrationIntegrationTests
{
    private static async Task AssertStagedGuidStorageTransitionAsync(
        IntegrationDatabaseTarget target
    )
    {
        // Arrange
        var connectionString = GetConnectionString(target);
        var serverVersion = IntegrationTestEnvironment.GetServerVersion(target);

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await CleanupAsync(connection).ConfigureAwait(false);

        try
        {
            // WHY: Capture each staged representation before the next schema transition replaces it.
            // Act
            await ExecuteRawAsync(
                    connection,
                    $"CREATE TABLE `{StorageTransitionTable}` ("
                    + "`Id` int NOT NULL AUTO_INCREMENT, "
                    + "`RequiredValue` char(36) NOT NULL, "
                    + "`OptionalValue` char(36) NULL, "
                    + $"CONSTRAINT `PK_{StorageTransitionTable}` PRIMARY KEY (`Id`));")
                .ConfigureAwait(false);

            await using (var charContext = new Char36StorageContext(
                             CreateOptions<Char36StorageContext>(
                                 connectionString,
                                 serverVersion,
                                 MySqlGuidFormat.Char36)))
            {
                charContext.Records.AddRange(
                    new GuidStorageRecord
                    {
                        RequiredValue = s_documentId,
                        OptionalValue = Guid.Empty,
                    },
                    new GuidStorageRecord
                    {
                        RequiredValue = s_secondDocumentId,
                    });

                await charContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            }

            await ExecuteRawAsync(
                    connection,
                    $"INSERT INTO `{StorageTransitionTable}` (`RequiredValue`, `OptionalValue`) "
                    + "VALUES ('not-a-guid', NULL);")
                .ConfigureAwait(false);
            await ExecuteRawAsync(
                    connection,
                    $"ALTER TABLE `{StorageTransitionTable}` "
                    + "ADD COLUMN `RequiredValueBinary` binary(16) NULL, "
                    + "ADD COLUMN `OptionalValueBinary` binary(16) NULL;")
                .ConfigureAwait(false);

            var malformedSources = await CountInvalidChar36SourceValuesAsync(connection).ConfigureAwait(false);

            await ExecuteRawAsync(
                    connection,
                    $"DELETE FROM `{StorageTransitionTable}` WHERE `RequiredValue` = 'not-a-guid';")
                .ConfigureAwait(false);

            var remainingMalformedSources = await CountInvalidChar36SourceValuesAsync(connection).ConfigureAwait(false);

            await ExecuteRawAsync(connection, CreateChar36ToBinaryBackfillSql()).ConfigureAwait(false);

            var invalidBinaryBackfills = await CountInvalidChar36BackfillsAsync(connection).ConfigureAwait(false);

            await ExecuteRawAsync(
                    connection,
                    $"ALTER TABLE `{StorageTransitionTable}` "
                    + "MODIFY COLUMN `RequiredValueBinary` binary(16) NOT NULL;")
                .ConfigureAwait(false);
            await ExecuteRawAsync(
                    connection,
                    $"ALTER TABLE `{StorageTransitionTable}` "
                    + "DROP COLUMN `RequiredValue`, DROP COLUMN `OptionalValue`, "
                    + "CHANGE COLUMN `RequiredValueBinary` `RequiredValue` binary(16) NOT NULL, "
                    + "CHANGE COLUMN `OptionalValueBinary` `OptionalValue` binary(16) NULL;")
                .ConfigureAwait(false);

            var binaryStorage = await ReadBinaryStorageAsync(connection).ConfigureAwait(false);
            GuidStorageRecord[] binaryRows;

            await using (var binaryContext = new Binary16StorageContext(
                             CreateOptions<Binary16StorageContext>(
                                 connectionString,
                                 serverVersion,
                                 MySqlGuidFormat.Binary16)))
            {
                binaryRows = await ReadStorageRowsAsync(binaryContext).ConfigureAwait(false);
            }

            await ExecuteRawAsync(
                    connection,
                    $"ALTER TABLE `{StorageTransitionTable}` "
                    + "ADD COLUMN `RequiredValueText` char(36) NULL, "
                    + "ADD COLUMN `OptionalValueText` char(36) NULL;")
                .ConfigureAwait(false);
            await ExecuteRawAsync(connection, CreateBinaryToChar36BackfillSql()).ConfigureAwait(false);

            var invalidTextBackfills = await CountInvalidBinaryBackfillsAsync(connection).ConfigureAwait(false);

            await ExecuteRawAsync(
                    connection,
                    $"ALTER TABLE `{StorageTransitionTable}` "
                    + "MODIFY COLUMN `RequiredValueText` char(36) NOT NULL;")
                .ConfigureAwait(false);
            await ExecuteRawAsync(
                    connection,
                    $"ALTER TABLE `{StorageTransitionTable}` "
                    + "DROP COLUMN `RequiredValue`, DROP COLUMN `OptionalValue`, "
                    + "CHANGE COLUMN `RequiredValueText` `RequiredValue` char(36) NOT NULL, "
                    + "CHANGE COLUMN `OptionalValueText` `OptionalValue` char(36) NULL;")
                .ConfigureAwait(false);

            var charStorage = await ReadChar36StorageAsync(connection).ConfigureAwait(false);

            await using var finalContext = new Char36StorageContext(
                CreateOptions<Char36StorageContext>(
                    connectionString,
                    serverVersion,
                    MySqlGuidFormat.Char36));

            var finalRows = await ReadStorageRowsAsync(finalContext).ConfigureAwait(false);

            // Assert
            Assert.Equal(1L, malformedSources);
            Assert.Equal(0L, remainingMalformedSources);
            Assert.Equal(0L, invalidBinaryBackfills);
            Assert.Equal(0L, invalidTextBackfills);
            Assert.Equal(Convert.ToHexString(s_documentId.ToByteArray(bigEndian: true)), binaryStorage.Value);
            Assert.Equal(["binary(16)", "binary(16)"], binaryStorage.Types);
            Assert.Equal(s_documentId.ToString("D", CultureInfo.InvariantCulture), charStorage.Value);
            Assert.Equal(["char(36)", "char(36)"], charStorage.Types);
            AssertStorageRows(binaryRows);
            AssertStorageRows(finalRows);
        }
        finally
        {
            await CleanupAsync(connection).ConfigureAwait(false);
        }
    }

    private static string CreateChar36ToBinaryBackfillSql() =>
        $"UPDATE `{StorageTransitionTable}` SET "
        + "`RequiredValueBinary` = UNHEX(REPLACE(TRIM(`RequiredValue`), '-', '')), "
        + "`OptionalValueBinary` = CASE WHEN `OptionalValue` IS NULL THEN NULL "
        + "ELSE UNHEX(REPLACE(TRIM(`OptionalValue`), '-', '')) END;";

    private static string CreateBinaryToChar36BackfillSql() =>
        $"UPDATE `{StorageTransitionTable}` SET "
        + $"`RequiredValueText` = {FormatBinaryGuid("`RequiredValue`")}, "
        + "`OptionalValueText` = CASE WHEN `OptionalValue` IS NULL THEN NULL "
        + $"ELSE {FormatBinaryGuid("`OptionalValue`")} END;";

    private static async Task<long> CountInvalidChar36SourceValuesAsync(
        MySqlConnection connection
    ) => await ExecuteLongScalarAsync(
            connection,
            $"SELECT COUNT(*) FROM `{StorageTransitionTable}` WHERE "
            + $"NOT ({IsCanonicalChar36("`RequiredValue`")}) OR "
            + $"(`OptionalValue` IS NOT NULL AND NOT ({IsCanonicalChar36("`OptionalValue`")}));")
        .ConfigureAwait(false);

    private static async Task<long> CountInvalidChar36BackfillsAsync(
        MySqlConnection connection
    ) => await ExecuteLongScalarAsync(
            connection,
            $"SELECT COUNT(*) FROM `{StorageTransitionTable}` WHERE "
            + $"(`RequiredValueBinary` IS NULL OR LOWER(TRIM(`RequiredValue`)) <> {FormatBinaryGuid("`RequiredValueBinary`")}) "
            + "OR (`OptionalValue` IS NOT NULL AND (`OptionalValueBinary` IS NULL "
            + $"OR LOWER(TRIM(`OptionalValue`)) <> {FormatBinaryGuid("`OptionalValueBinary`")}));")
        .ConfigureAwait(false);

    private static async Task<long> CountInvalidBinaryBackfillsAsync(
        MySqlConnection connection
    ) => await ExecuteLongScalarAsync(
            connection,
            $"SELECT COUNT(*) FROM `{StorageTransitionTable}` WHERE "
            + "(`RequiredValueText` IS NULL OR `RequiredValue` <> UNHEX(REPLACE(`RequiredValueText`, '-', '')) "
            + $"OR `RequiredValueText` <> {FormatBinaryGuid("`RequiredValue`")}) "
            + "OR (`OptionalValue` IS NOT NULL AND (`OptionalValueText` IS NULL "
            + "OR `OptionalValue` <> UNHEX(REPLACE(`OptionalValueText`, '-', '')) "
            + $"OR `OptionalValueText` <> {FormatBinaryGuid("`OptionalValue`")}));")
        .ConfigureAwait(false);

    private static string FormatBinaryGuid(
        string expression
    ) => $"LOWER(CONCAT(SUBSTRING(HEX({expression}), 1, 8), '-', "
        + $"SUBSTRING(HEX({expression}), 9, 4), '-', SUBSTRING(HEX({expression}), 13, 4), '-', "
        + $"SUBSTRING(HEX({expression}), 17, 4), '-', SUBSTRING(HEX({expression}), 21, 12)))";

    private static string IsCanonicalChar36(
        string expression
    ) => $"LOWER(TRIM({expression})) REGEXP "
        + "'^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'";

    private static async Task<(string? Value, IReadOnlyList<string> Types)> ReadBinaryStorageAsync(
        MySqlConnection connection
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT HEX(`RequiredValue`) FROM `{StorageTransitionTable}` ORDER BY `Id` LIMIT 1;";

        var value = Convert.ToString(
                await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false),
                CultureInfo.InvariantCulture);

        var types = await ReadColumnTypesAsync(connection).ConfigureAwait(false);

        return (value, types);
    }

    private static async Task<(string? Value, IReadOnlyList<string> Types)> ReadChar36StorageAsync(
        MySqlConnection connection
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT `RequiredValue` FROM `{StorageTransitionTable}` ORDER BY `Id` LIMIT 1;";

        var value = Convert.ToString(
                await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false),
                CultureInfo.InvariantCulture);

        var types = await ReadColumnTypesAsync(connection).ConfigureAwait(false);

        return (value, types);
    }

    private static async Task<IReadOnlyList<string>> ReadColumnTypesAsync(
        MySqlConnection connection
    )
    {

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT `COLUMN_TYPE` FROM information_schema.COLUMNS "
            + "WHERE `TABLE_SCHEMA` = DATABASE() AND `TABLE_NAME` = @tableName "
            + "AND `COLUMN_NAME` IN ('RequiredValue', 'OptionalValue') ORDER BY `COLUMN_NAME`;";
        command.Parameters.AddWithValue("@tableName", StorageTransitionTable);

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(false);
        var columnTypes = new List<string>();

        while (await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false))
        {
            columnTypes.Add(reader.GetString(0));
        }

        return columnTypes;
    }

    private static async Task<GuidStorageRecord[]> ReadStorageRowsAsync(
        GuidStorageContext context
    )
    {
        return await context
            .Records.AsNoTracking()
            .OrderBy(record => record.Id)
            .ToArrayAsync(CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static void AssertStorageRows(
        IReadOnlyList<GuidStorageRecord> rows
    )
    {
        Assert.Collection(
            rows,
            first =>
            {
                Assert.Equal(s_documentId, first.RequiredValue);
                Assert.Equal(Guid.Empty, first.OptionalValue);
            },
            second =>
            {
                Assert.Equal(s_secondDocumentId, second.RequiredValue);
                Assert.Null(second.OptionalValue);
            });
    }
}
