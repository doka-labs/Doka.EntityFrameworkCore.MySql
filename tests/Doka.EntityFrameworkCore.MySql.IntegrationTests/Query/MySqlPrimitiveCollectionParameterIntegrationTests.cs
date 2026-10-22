namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies parameterized primitive collections against the stored provider
/// representation on every supported database engine.
/// </summary>
[IntegrationDatabaseTest]
public sealed partial class MySqlPrimitiveCollectionParameterIntegrationTests :
    IClassFixture<MySqlPrimitiveCollectionParameterIntegrationTests.SeededDatabaseFixture>
{
    private const string TableName = "IntPrimitiveCollectionItems";
    private const string CharTableName = "IntCharGuidCollectionItems";
    private const string SqlModeTableName = "IntSqlModeStringCollectionItems";
    private const int RowCount = 2200;

    private readonly SeededDatabaseFixture _fixture;

    /// <summary>Returns one independently reported case per selected supported engine.</summary>
    // WHY: MySql80 is a legacy opt-in target, not part of this six-engine contract.
    // The companion theory attribute skips a run that selects only that target.
    public static IEnumerable<object[]> SelectedTargets => IntegrationTestEnvironment
        .GetSelectedTargets()
        .Where(target => IntegrationTestEnvironment.GetSupportedTargets().Contains(target))
        .Select(target => new object[] { target.ToString() });

    /// <summary>Creates a test case with the shared per-engine seed fixture.</summary>
    /// <param name="fixture">The class-owned, lazily seeded database fixture.</param>
    public MySqlPrimitiveCollectionParameterIntegrationTests(
        SeededDatabaseFixture fixture
    )
    {
        _fixture = fixture;
    }
}
