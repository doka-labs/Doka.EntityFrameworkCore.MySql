namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMigrationOperationHandlerTests
{
    /// <summary>
    /// Gets generation modes, including combined flags and an unknown future flag.
    /// </summary>
    public static TheoryData<MigrationsSqlGenerationOptions> GenerationModes =>
    [
        MigrationsSqlGenerationOptions.Default,
        MigrationsSqlGenerationOptions.Script,
        MigrationsSqlGenerationOptions.Idempotent,
        MigrationsSqlGenerationOptions.NoTransactions,
        MigrationsSqlGenerationOptions.Script | MigrationsSqlGenerationOptions.Idempotent,
        MigrationsSqlGenerationOptions.Script | MigrationsSqlGenerationOptions.NoTransactions,
        MigrationsSqlGenerationOptions.Idempotent | MigrationsSqlGenerationOptions.NoTransactions,
        MigrationsSqlGenerationOptions.Script
        | MigrationsSqlGenerationOptions.Idempotent
        | MigrationsSqlGenerationOptions.NoTransactions,
        (MigrationsSqlGenerationOptions)8,
    ];

    /// <summary>
    /// Gets one independently selectable case per engine profile and reserved operation type.
    /// </summary>
    public static TheoryData<string, Type> BaselineOperationCases
    {
        get
        {
            var cases = new TheoryData<string, Type>();
            var serverVersions = ActiveLtsServerVersions;
            var operationTypes = CreateStandardOperationFixtures().Select(operation => operation.GetType()).ToArray();

            // Recreate mutable operations inside each test instead of sharing them across cases.
            foreach (var serverVersion in serverVersions)
            {
                foreach (var operationType in operationTypes)
                {
                    cases.Add(serverVersion.ToString(), operationType);
                }
            }

            return cases;
        }
    }

    private static IReadOnlyList<MySqlServerVersion> ActiveLtsServerVersions =>
    [
        MySqlServerVersion.MySql(new Version(8, 4, 11)),
        MySqlServerVersion.MySql(new Version(9, 7, 2)),
        MySqlServerVersion.MariaDb(new Version(10, 11, 18)),
        MySqlServerVersion.MariaDb(new Version(11, 4, 12)),
        MySqlServerVersion.MariaDb(new Version(11, 8, 8)),
        MySqlServerVersion.MariaDb(new Version(12, 3, 2)),
    ];
}
