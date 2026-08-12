using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Doka.EntityFrameworkCore.MySql.Examples.MigrationsWorkflow.Migrations;

/// <inheritdoc />
public partial class InitialMigration : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateMigrationHandlerEvidence();

        migrationBuilder.CreateTable(
            name: "MigrationWorkflowItems",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.AutoIncrement),
                Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MigrationWorkflowItems", x => x.Id);
            });

        migrationBuilder.InsertData(
            table: "MigrationWorkflowItems",
            columns:
            [
                "Id",
                "Name",
            ],
            values:
            [
                1,
                "migration-safety-readback",
            ]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "MigrationWorkflowItems");

        migrationBuilder.DropMigrationHandlerEvidence();
    }
}
