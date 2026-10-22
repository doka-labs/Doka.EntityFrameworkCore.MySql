namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies scalar, aggregate and JSON translations against their existing live-server targets.
/// </summary>
[IntegrationDatabaseTest]
public sealed partial class MySqlQueryTranslationIntegrationTests
{
    private const string TableName = "IntQueryItems";

    // WHY: CallerFilePath owns the test schema. Keep its original source-file
    // identity when topic partials move the individual test methods.
    private static string GetConnectionString(
        IntegrationDatabaseTarget target
    ) => IntegrationTestEnvironment.GetConnectionString(target);
}
