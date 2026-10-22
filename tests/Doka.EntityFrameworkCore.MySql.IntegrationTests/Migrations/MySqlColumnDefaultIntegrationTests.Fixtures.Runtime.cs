namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

internal static class TimestampRepairMigrationContract
{
    /// <summary>
    /// The history table used by this migration fixture.
    /// </summary>
    public const string HistoryTable = "__DokaTimestampRepairHistory";

    /// <summary>
    /// The initial migration used by this migration fixture.
    /// </summary>
    public const string InitialMigration = "20260821000000_TimestampRepairInitial";

    /// <summary>
    /// The invalid migration used by this migration fixture.
    /// </summary>
    public const string InvalidMigration = "20260821000100_TimestampRepairInvalid";

    /// <summary>
    /// The table used by this migration fixture.
    /// </summary>
    public const string Table = "DokaTimestampRepairProbe";
}

internal sealed class TimestampRepairMigrationContext : DbContext
{
    /// <summary>
    /// Initializes the TimestampRepairMigrationContext fixture.
    /// </summary>
    public TimestampRepairMigrationContext(
        DbContextOptions<TimestampRepairMigrationContext> options
    ) : base(options) { }
}

[DbContext(typeof(TimestampRepairMigrationContext))]
[Migration(TimestampRepairMigrationContract.InitialMigration)]
internal sealed class TimestampRepairInitialMigration : Migration
{
    /// <inheritdoc />
    protected override void Up(
        MigrationBuilder migrationBuilder
    )
    {
        migrationBuilder.CreateTable(
            name: TimestampRepairMigrationContract.Table,
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false),
                OccurredAt = table.Column<DateTime>(type: "timestamp(6)", nullable: true),
            },
            constraints: table =>
                table.PrimaryKey("PK_DokaTimestampRepairProbe", column => column.Id));

        migrationBuilder.InsertData(
            table: TimestampRepairMigrationContract.Table,
            columns: ["Id", "OccurredAt"],
            columnTypes: ["int", "timestamp(6)"],
            values: [1, null]);
    }

    /// <inheritdoc />
    protected override void Down(
        MigrationBuilder migrationBuilder
    ) => migrationBuilder.DropTable(TimestampRepairMigrationContract.Table);
}

[DbContext(typeof(TimestampRepairMigrationContext))]
[Migration(TimestampRepairMigrationContract.InvalidMigration)]
internal sealed class TimestampRepairInvalidMigration : Migration
{
    /// <inheritdoc />
    protected override void Up(
        MigrationBuilder migrationBuilder
    ) => migrationBuilder.AlterColumn<DateTime>(
        name: "OccurredAt",
        table: TimestampRepairMigrationContract.Table,
        type: "timestamp(6)",
        nullable: false,
        oldClrType: typeof(DateTime),
        oldType: "timestamp(6)",
        oldNullable: true);

    /// <inheritdoc />
    protected override void Down(
        MigrationBuilder migrationBuilder
    ) => migrationBuilder.AlterColumn<DateTime>(
        name: "OccurredAt",
        table: TimestampRepairMigrationContract.Table,
        type: "timestamp(6)",
        nullable: true,
        oldClrType: typeof(DateTime),
        oldType: "timestamp(6)");
}
