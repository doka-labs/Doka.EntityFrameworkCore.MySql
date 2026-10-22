namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlColumnDefaultIntegrationTests
{
    private static CreateTableOperation CreateTableOperation(
        string tableName
    )
    {
        var operation = new CreateTableOperation { Name = tableName };
        operation.Columns.Add(
            new AddColumnOperation
            {
                Table = tableName,
                Name = "Id",
                ClrType = typeof(int),
                ColumnType = "int",
                IsNullable = false,
            });
        operation.Columns.Add(
            CreateColumn(
                tableName,
                "DateValue",
                typeof(DateOnly),
                "date",
                new DateOnly(2026, 8, 17)));
        operation.Columns.Add(
            CreateColumn(
                tableName,
                "TimeValue",
                typeof(TimeOnly),
                "time(6)",
                new TimeOnly(12, 34, 56).Add(TimeSpan.FromTicks(1_234_567))));
        operation.Columns.Add(
            CreateColumn(tableName, "CharValue", typeof(char), "char(1)", '\\'));
        operation.Columns.Add(
            CreateColumn(
                tableName,
                "DurationValue",
                typeof(TimeSpan),
                "time(6)",
                TimeSpan.FromHours(27) + TimeSpan.FromTicks(1_234_567)));
        operation.Columns.Add(
            CreateColumn(
                tableName,
                "MaximumDurationValue",
                typeof(TimeSpan),
                "time(6)",
                s_maximumTime));
        operation.Columns.Add(
            CreateColumn(
                tableName,
                "MinimumDurationValue",
                typeof(TimeSpan),
                "time(6)",
                -s_maximumTime));
        var jsonColumn = CreateColumn(tableName, "JsonValue", typeof(string), "json", "{}");
        jsonColumn.Comment = "json\\comment";
        jsonColumn.SetAnnotation(MySqlAnnotationNames.Invisible, true);
        operation.Columns.Add(jsonColumn);
        operation.Columns.Add(
            CreateColumn(tableName, "GuidValue", typeof(Guid), "binary(16)", s_guidDefault));
        operation.Columns.Add(
            new AddColumnOperation
            {
                Table = tableName,
                Name = "RepairDate",
                ClrType = typeof(DateOnly),
                ColumnType = "date",
                IsNullable = true,
            });
        operation.Columns.Add(
            new AddColumnOperation
            {
                Table = tableName,
                Name = "RepairDateTime",
                ClrType = typeof(DateTime),
                ColumnType = "datetime(6)",
                IsNullable = true,
            });
        operation.Columns.Add(
            new AddColumnOperation
            {
                Table = tableName,
                Name = "RepairTimestamp",
                ClrType = typeof(DateTime),
                ColumnType = "timestamp(6)",
                IsNullable = true,
            });
        operation.Columns.Add(
            new AddColumnOperation
            {
                Table = tableName,
                Name = "InvalidRepairTimestamp",
                ClrType = typeof(DateTime),
                ColumnType = "timestamp(6)",
                IsNullable = true,
            });
        operation.Columns.Add(
            new AddColumnOperation
            {
                Table = tableName,
                Name = "RepairJson",
                ClrType = typeof(string),
                ColumnType = "json",
                IsNullable = true,
            });
        operation.Columns.Add(
            new AddColumnOperation
            {
                Table = tableName,
                Name = "RepairSpatial",
                ClrType = typeof(byte[]),
                ColumnType = "point",
                IsNullable = true,
            });
        operation.Columns.Add(
            new AddColumnOperation
            {
                Table = tableName,
                Name = "RepairEnum",
                ClrType = typeof(string),
                ColumnType = "enum('alpha','beta')",
                IsNullable = true,
            });
        operation.Columns.Add(
            new AddColumnOperation
            {
                Table = tableName,
                Name = "RepairChecked",
                ClrType = typeof(int),
                ColumnType = "int",
                IsNullable = true,
            });
        operation.CheckConstraints.Add(
            new AddCheckConstraintOperation
            {
                Name = $"CK_{tableName}_RepairChecked",
                Table = tableName,
                Sql = "`RepairChecked` > 0",
            });
        var spatialColumn = new AddColumnOperation
        {
            Table = tableName,
            Name = "SpatialValue",
            ClrType = typeof(byte[]),
            ColumnType = "point",
            IsNullable = false,
            DefaultValueSql = "ST_GeomFromText('POINT(1 2)', 4326)",
            Comment = "spatial metadata",
        };

        spatialColumn.SetAnnotation(MySqlAnnotationNames.SpatialReferenceSystemId, 4326);
        spatialColumn.SetAnnotation(MySqlAnnotationNames.Invisible, true);
        operation.Columns.Add(spatialColumn);

        return operation;
    }

    private static AddColumnOperation CreateColumn(
        string tableName,
        string name,
        Type clrType,
        string columnType,
        object defaultValue
    )
    {
        return new AddColumnOperation
        {
            Table = tableName,
            Name = name,
            ClrType = clrType,
            ColumnType = columnType,
            IsNullable = false,
            DefaultValue = defaultValue,
        };
    }
}
