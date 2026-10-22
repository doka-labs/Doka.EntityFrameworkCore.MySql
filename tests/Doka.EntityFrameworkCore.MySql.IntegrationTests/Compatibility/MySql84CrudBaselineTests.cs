namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies the live MySQL 8.4 CRUD, migration and database-creator baseline.
/// </summary>
[IntegrationDatabaseTest]
public sealed partial class MySql84CrudBaselineTests
{
    private const string CrudTableName = "Phase1CrudEntities";
    private const string MigrationProbeTableName = "Phase1MigrationProbe";
    private const string HistoryTableName = "__EFMigrationsHistory";
    private const string BinaryGuidTableName = "Phase2BinaryGuidEntities";
    private const string CharGuidTableName = "Phase2CharGuidEntities";
    private const string JsonTableName = "Phase2JsonEntities";

    // WHY: CallerFilePath owns the test schema. Keep the original owner for
    // every topic partial that creates and resets these shared baseline tables.
    private static string GetConnectionString() => IntegrationTestEnvironment
        .GetConnectionString(IntegrationDatabaseTarget.MySql84);
}
