using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Doka.EntityFrameworkCore.MySql.Examples.MigrationsWorkflow.Migrations;

/// <inheritdoc />
public partial class AddTemporalDefaults : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateOnly>(
            name: "EffectiveDate",
            table: "MigrationWorkflowItems",
            type: "date",
            nullable: false,
            defaultValue: new DateOnly(2026, 8, 17));

        migrationBuilder.AddColumn<TimeOnly>(
            name: "EffectiveTime",
            table: "MigrationWorkflowItems",
            type: "time(6)",
            precision: 6,
            nullable: false,
            defaultValue: new TimeOnly(12, 34, 56, 123, 456));

        migrationBuilder.InsertData(
            table: "MigrationWorkflowItems",
            columns:
            [
                "Id",
                "Name",
            ],
            values:
            [
                2,
                "added-default-readback",
            ]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DeleteData(
            table: "MigrationWorkflowItems",
            keyColumn: "Id",
            keyValue: 2);

        migrationBuilder.DropColumn(
            name: "EffectiveDate",
            table: "MigrationWorkflowItems");

        migrationBuilder.DropColumn(
            name: "EffectiveTime",
            table: "MigrationWorkflowItems");
    }
}
