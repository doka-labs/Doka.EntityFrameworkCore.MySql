using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Doka.EntityFrameworkCore.MySql.Examples.MigrationsWorkflow.Migrations;

/// <inheritdoc />
public partial class UpdateTemporalDefaults : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<DateOnly>(
            name: "EffectiveDate",
            table: "MigrationWorkflowItems",
            type: "date",
            nullable: false,
            defaultValue: new DateOnly(2028, 2, 3),
            oldClrType: typeof(DateOnly),
            oldType: "date",
            oldDefaultValue: new DateOnly(2026, 8, 17));

        migrationBuilder.AlterColumn<TimeOnly>(
            name: "EffectiveTime",
            table: "MigrationWorkflowItems",
            type: "time(6)",
            precision: 6,
            nullable: false,
            defaultValue: new TimeOnly(4, 5, 6, 654, 321),
            oldClrType: typeof(TimeOnly),
            oldType: "time(6)",
            oldPrecision: 6,
            oldDefaultValue: new TimeOnly(12, 34, 56, 123, 456));

        migrationBuilder.InsertData(
            table: "MigrationWorkflowItems",
            columns:
            [
                "Id",
                "Name",
            ],
            values:
            [
                3,
                "altered-default-readback",
            ]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DeleteData(
            table: "MigrationWorkflowItems",
            keyColumn: "Id",
            keyValue: 3);

        migrationBuilder.AlterColumn<DateOnly>(
            name: "EffectiveDate",
            table: "MigrationWorkflowItems",
            type: "date",
            nullable: false,
            defaultValue: new DateOnly(2026, 8, 17),
            oldClrType: typeof(DateOnly),
            oldType: "date",
            oldDefaultValue: new DateOnly(2028, 2, 3));

        migrationBuilder.AlterColumn<TimeOnly>(
            name: "EffectiveTime",
            table: "MigrationWorkflowItems",
            type: "time(6)",
            precision: 6,
            nullable: false,
            defaultValue: new TimeOnly(12, 34, 56, 123, 456),
            oldClrType: typeof(TimeOnly),
            oldType: "time(6)",
            oldPrecision: 6,
            oldDefaultValue: new TimeOnly(4, 5, 6, 654, 321));
    }
}
