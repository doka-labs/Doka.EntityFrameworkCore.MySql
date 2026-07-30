using System.Reflection;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification;

/// <summary>
/// Enforces that this assembly closes every public EF Core specification-test
/// base without a provider-owned ignore list.
/// </summary>
[Trait("Category", "Spec")]
public sealed class MySqlComplianceTest : RelationalComplianceTestBase
{
    protected override Assembly TargetAssembly { get; } = typeof(MySqlComplianceTest).Assembly;
}
