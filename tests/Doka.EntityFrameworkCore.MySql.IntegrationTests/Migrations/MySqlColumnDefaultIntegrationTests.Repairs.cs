namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlColumnDefaultIntegrationTests
{
    private static async Task AssertNullableTemporalRepairAsync(
        IMigrationsSqlGenerator generator,
        IModel model,
        IRelationalConnection relationalConnection,
        MySqlConnection connection,
        string tableName
    )
    {
        // Arrange
        var timestampRepair = CreateRequiredColumnRepair<DateTime>(
            tableName,
            "RepairTimestamp",
            "timestamp(6)");

        timestampRepair.DefaultValueSql = "CURRENT_TIMESTAMP(6)";

        MigrationOperation[] operations =
        [
            CreateRequiredColumnRepair<DateOnly>(
                tableName,
                "RepairDate",
                "date",
                new DateOnly(2026, 8, 21)),
            CreateRequiredColumnRepair<DateTime>(
                tableName,
                "RepairDateTime",
                "datetime(6)",
                new DateTime(2026, 8, 21, 12, 34, 56)),
            timestampRepair,
        ];

        // Act
        await ExecuteAsync(generator, model, relationalConnection, operations)
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM `{tableName}` "
            + "WHERE `RepairDate` = DATE '2026-08-21' "
            + "AND `RepairDateTime` = TIMESTAMP '2026-08-21 12:34:56' "
            + "AND `RepairTimestamp` IS NOT NULL;";

        var repairedRows = Convert.ToInt64(
                await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false),
                CultureInfo.InvariantCulture);

        var invalidRepair = CreateRequiredColumnRepair<DateTime>(
            tableName,
            "InvalidRepairTimestamp",
            "timestamp(6)");

        invalidRepair.DefaultValueSql = "TIMESTAMP '0001-01-01 00:00:00'";

        var exception = await Assert.ThrowsAsync<MySqlException>(() =>
            ExecuteAsync(generator, model, relationalConnection, [invalidRepair]));

        command.CommandText = "SELECT IS_NULLABLE FROM information_schema.COLUMNS "
            + "WHERE TABLE_SCHEMA = DATABASE() "
            + $"AND TABLE_NAME = '{tableName}' "
            + "AND COLUMN_NAME = 'InvalidRepairTimestamp';";

        var invalidColumnNullability = Convert.ToString(
                await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false),
                CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(2L, repairedRows);
        Assert.Equal(1292, exception.Number);
        Assert.Equal("YES", invalidColumnNullability);
    }

    private static async Task AssertConstrainedNullableRepairAsync(
        IMigrationsSqlGenerator generator,
        IModel model,
        IRelationalConnection relationalConnection,
        MySqlConnection connection,
        string tableName
    )
    {
        // Arrange
        AlterColumnOperation[] implicitRepairs =
        [
            CreateRequiredColumnRepair<string>(tableName, "RepairJson", "json"),
            CreateRequiredColumnRepair<byte[]>(tableName, "RepairSpatial", "point"),
            CreateRequiredColumnRepair<string>(tableName, "RepairEnum", "enum('alpha','beta')"),
            CreateRequiredColumnRepair<int>(tableName, "RepairChecked", "int"),
        ];

        var implicitFailures = new List<(string Name, InvalidOperationException Exception)>();
        var jsonRepair = CreateRequiredColumnRepair<string>(
            tableName,
            "RepairJson",
            "json",
            "{}");

        var spatialRepair = CreateRequiredColumnRepair<byte[]>(
            tableName,
            "RepairSpatial",
            "point");

        spatialRepair.DefaultValueSql = "ST_GeomFromText('POINT(0 0)')";
        var enumRepair = CreateRequiredColumnRepair<string>(
            tableName,
            "RepairEnum",
            "enum('alpha','beta')",
            "alpha");

        var checkedRepair = CreateRequiredColumnRepair<int>(
            tableName,
            "RepairChecked",
            "int",
            1);

        // Act
        foreach (var implicitRepair in implicitRepairs)
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                generator.Generate([implicitRepair], model));

            implicitFailures.Add((implicitRepair.Name, exception));
        }

        await ExecuteAsync(
                generator,
                model,
                relationalConnection,
                [jsonRepair, spatialRepair, enumRepair, checkedRepair])
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM `{tableName}` "
            + "WHERE JSON_LENGTH(`RepairJson`) = 0 "
            + "AND ST_X(`RepairSpatial`) = 0 "
            + "AND ST_Y(`RepairSpatial`) = 0 "
            + "AND `RepairEnum` = 'alpha' "
            + "AND `RepairChecked` = 1;";

        // Assert
        Assert.All(implicitFailures, failure =>
        {
            Assert.Contains("application contract", failure.Exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(failure.Name, failure.Exception.Message, StringComparison.Ordinal);
        });
        Assert.Equal(
            2L,
            Convert.ToInt64(
                await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false),
                CultureInfo.InvariantCulture));
    }

    private static AlterColumnOperation CreateRequiredColumnRepair<TValue>(
        string tableName,
        string columnName,
        string columnType,
        object? defaultValue = null
    )
    {
        return new AlterColumnOperation
        {
            Table = tableName,
            Name = columnName,
            ClrType = typeof(TValue),
            ColumnType = columnType,
            IsNullable = false,
            DefaultValue = defaultValue,
            OldColumn =
            {
                ClrType = typeof(TValue),
                ColumnType = columnType,
                IsNullable = true,
            },
        };
    }
}
