using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Microsoft.EntityFrameworkCore.Query.Associations.ComplexJson;
using Microsoft.EntityFrameworkCore.Query.Associations.ComplexTableSplitting;
using Microsoft.EntityFrameworkCore.Query.Associations.OwnedJson;
using Xunit.Abstractions;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.BulkUpdates;

/// <summary>
/// Executes the official complex-property JSON bulk-update contract.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class ComplexJsonBulkUpdateMySqlTest
    : ComplexJsonBulkUpdateRelationalTestBase<
        ComplexJsonBulkUpdateMySqlFixture>
{
    public ComplexJsonBulkUpdateMySqlTest(
        ComplexJsonBulkUpdateMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(
        fixture,
        testOutputHelper)
    {
    }
}

/// <summary>
/// MySQL fixture for complex properties stored as JSON.
/// </summary>
public sealed class ComplexJsonBulkUpdateMySqlFixture
    : ComplexJsonRelationalFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory =>
        MySqlTestStoreFactory.Instance;
}

/// <summary>
/// Executes the official complex-property table-splitting bulk-update
/// contract.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class ComplexTableSplittingBulkUpdateMySqlTest
    : ComplexTableSplittingBulkUpdateRelationalTestBase<
        ComplexTableSplittingBulkUpdateMySqlFixture>
{
    public ComplexTableSplittingBulkUpdateMySqlTest(
        ComplexTableSplittingBulkUpdateMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(
        fixture,
        testOutputHelper)
    {
    }
}

/// <summary>
/// MySQL fixture for complex-property table splitting.
/// </summary>
public sealed class ComplexTableSplittingBulkUpdateMySqlFixture
    : ComplexTableSplittingRelationalFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory =>
        MySqlTestStoreFactory.Instance;
}

/// <summary>
/// Executes the official owned-JSON bulk-update rejection and diagnostic
/// contract.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class OwnedJsonBulkUpdateMySqlTest
    : OwnedJsonBulkUpdateRelationalTestBase<
        OwnedJsonBulkUpdateMySqlFixture>
{
    public OwnedJsonBulkUpdateMySqlTest(
        OwnedJsonBulkUpdateMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(
        fixture,
        testOutputHelper)
    {
    }
}

/// <summary>
/// MySQL fixture for owned navigations stored as JSON.
/// </summary>
public sealed class OwnedJsonBulkUpdateMySqlFixture
    : OwnedJsonRelationalFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory =>
        MySqlTestStoreFactory.Instance;
}
