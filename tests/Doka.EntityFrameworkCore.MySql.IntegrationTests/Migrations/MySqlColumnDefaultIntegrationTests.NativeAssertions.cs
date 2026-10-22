namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlColumnDefaultIntegrationTests
{
    private static async Task AssertInitialDefaultsAsync(
        MySqlConnection connection,
        string tableName
    )
    {
        // Arrange
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DATE_FORMAT(`DateValue`, '%Y-%m-%d'), "
            + "TIME_FORMAT(`TimeValue`, '%H:%i:%s.%f'), HEX(`CharValue`), "
            + "TIME_FORMAT(`DurationValue`, '%H:%i:%s.%f'), "
            + "CAST(`MaximumDurationValue` AS CHAR), CAST(`MinimumDurationValue` AS CHAR), "
            + "JSON_LENGTH(`JsonValue`), HEX(`GuidValue`), "
            + "ST_X(`SpatialValue`), ST_Y(`SpatialValue`), ST_SRID(`SpatialValue`) "
            + $"FROM `{tableName}` WHERE `Id` = 1;";

        // Act
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.True(await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false));
        Assert.Equal("2026-08-17", reader.GetString(0));
        Assert.Equal("12:34:56.123456", reader.GetString(1));
        Assert.Equal("5C", reader.GetString(2));
        Assert.Equal("27:00:00.123456", reader.GetString(3));
        Assert.Equal("838:59:59.000000", reader.GetString(4));
        Assert.Equal("-838:59:59.000000", reader.GetString(5));
        Assert.Equal(0L, reader.GetInt64(6));
        Assert.Equal(GuidDefaultHex, reader.GetString(7));
        Assert.Equal(1D, reader.GetDouble(8));
        Assert.Equal(2D, reader.GetDouble(9));
        Assert.Equal(4326L, reader.GetInt64(10));
    }

    private static async Task AssertJsonMetadataAsync(
        MySqlConnection connection,
        string tableName
    )
    {
        // Arrange
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COLUMN_COMMENT, EXTRA FROM information_schema.COLUMNS "
            + "WHERE TABLE_SCHEMA = DATABASE() "
            + "AND TABLE_NAME = @tableName "
            + "AND COLUMN_NAME = 'JsonValue';";
        command.Parameters.AddWithValue("@tableName", tableName);

        // Act
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.True(await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false));
        Assert.Equal("json\\comment", reader.GetString(0));
        Assert.Contains("INVISIBLE", reader.GetString(1), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task AssertGuidDefaultMetadataAsync(
        MySqlConnection connection,
        string tableName,
        MySqlServerVersion serverVersion
    )
    {
        // Arrange
        await using var catalogCommand = connection.CreateCommand();
        catalogCommand.CommandText = "SELECT HEX(COLUMN_DEFAULT), OCTET_LENGTH(COLUMN_DEFAULT) "
            + "FROM information_schema.COLUMNS "
            + "WHERE TABLE_SCHEMA = DATABASE() "
            + "AND TABLE_NAME = @tableName "
            + "AND COLUMN_NAME = 'GuidValue';";
        catalogCommand.Parameters.AddWithValue("@tableName", tableName);

        await using (var reader = await catalogCommand.ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(false))
        {
            Assert.True(await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false));

            var catalogDefaultHex = reader.GetString(0);
            var catalogLength = reader.GetInt64(1);

            if (!serverVersion.IsMariaDb)
            {
                Assert.Equal(
                    "30783031393842464532353537333730",
                    catalogDefaultHex,
                    ignoreCase: true);
                Assert.Equal(16L, catalogLength);
            }
            else if (serverVersion.Version >= new Version(11, 8))
            {
                var expectedCatalogText = $"x'{GuidDefaultHex.ToLowerInvariant()}'";
                var expectedCatalogHex = Convert.ToHexString(
                    expectedCatalogText.Select(static character => (byte)character).ToArray());

                Assert.Equal(expectedCatalogHex, catalogDefaultHex, ignoreCase: true);
                Assert.Equal(expectedCatalogText.Length, catalogLength);
            }
            else
            {
                var completeTextHex = Convert.ToHexString(
                    GuidDefaultHex
                        .ToLowerInvariant()
                        .Select(static character => (byte)character)
                        .ToArray());

                Assert.DoesNotContain(GuidDefaultHex, catalogDefaultHex, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(completeTextHex, catalogDefaultHex, StringComparison.OrdinalIgnoreCase);
                Assert.True(catalogLength < 35L);
            }
        }

        await using var showCreateCommand = connection.CreateCommand();
        showCreateCommand.CommandText = $"SHOW CREATE TABLE `{tableName}`;";

        // Act
        await using var showCreateReader = await showCreateCommand
            .ExecuteReaderAsync(CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        Assert.True(await showCreateReader.ReadAsync(CancellationToken.None).ConfigureAwait(false));
        var createTableSql = showCreateReader.GetString(1);

        if (serverVersion.IsMariaDb
            && serverVersion.Version < new Version(11, 8))
        {
            Assert.DoesNotContain(GuidDefaultHex, createTableSql, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Contains(GuidDefaultHex, createTableSql, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static async Task AssertAddedDefaultsAsync(
        MySqlConnection connection,
        string tableName,
        int id,
        string expectedTime
    )
    {
        // Arrange
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DATE_FORMAT(`AddedDate`, '%Y-%m-%d'), "
            + $"TIME_FORMAT(`AddedTime`, '%H:%i:%s.%f') FROM `{tableName}` WHERE `Id` = @id;";
        command.Parameters.AddWithValue("@id", id);

        // Act
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.True(await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false));
        Assert.Equal("2027-01-02", reader.GetString(0));
        Assert.Equal(expectedTime, reader.GetString(1));
    }
}
