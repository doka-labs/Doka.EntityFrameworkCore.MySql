using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query;

/// <summary>
/// Activates an upstream flaky-test skip after the complete provider matrix proved
/// deterministic SaveChanges behavior.
/// </summary>
public sealed partial class AdHocMiscellaneousQueryMySqlTest
{
    [Fact]
    public override Task SaveChangesAsync_accepts_changes_with_ConfigureAwait_true() =>
        base.SaveChangesAsync_accepts_changes_with_ConfigureAwait_true();
}

/// <summary>
/// Runs the complex-type pushdown contract on MySQL and records MariaDB's existing
/// correlated-derived-table engine boundary.
/// </summary>
public sealed partial class ComplexTypeQueryMySqlTest
{
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Same_complex_type_projected_twice_with_pushdown_as_part_of_another_projection(
        bool async
    ) => base.Same_complex_type_projected_twice_with_pushdown_as_part_of_another_projection(async);
}

/// <summary>
/// Activates value-converter-aware primitive collection parameter serialization,
/// which passes on every supported provider target.
/// </summary>
public sealed partial class NonSharedPrimitiveCollectionsQueryMySqlTest
{
    [Fact]
    public override Task Parameter_with_inferred_value_converter() => base.Parameter_with_inferred_value_converter();
}

/// <summary>
/// Records the rowset-function boundary for the remaining upstream-skipped UDF
/// contract instead of inheriting an untracked framework skip.
/// </summary>
public sealed partial class UdfDbFunctionMySqlTest
{
    [SpecEngineLimitationFact("MYSQL-MARIADB-SCALAR-STORED-FUNCTIONS", "mysql84", "mariadb114", "mariadb118")]
    public override void QF_Select_Direct_In_Anonymous_distinct() => base.QF_Select_Direct_In_Anonymous_distinct();
}
