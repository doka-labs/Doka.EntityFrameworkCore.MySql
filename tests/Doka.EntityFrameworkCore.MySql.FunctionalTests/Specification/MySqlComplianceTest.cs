namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification;

/// <summary>
/// Enforces that this assembly closes every public EF Core specification-test
/// base through inheritance or an explicitly validated structural facade.
/// </summary>
[Trait("Category", "Spec")]
public sealed class MySqlComplianceTest : RelationalComplianceTestBase
{
    // xUnit 4 cannot execute the overloaded public tests inherited from this
    // contract. The specification inventory instead verifies every signature
    // on StoredProcedureUpdateMySqlTest before publication.
    protected override ICollection<Type> IgnoredTestBases { get; } =
    [
        typeof(StoredProcedureUpdateTestBase),
    ];

    protected override Assembly TargetAssembly { get; } = typeof(MySqlComplianceTest).Assembly;
}
