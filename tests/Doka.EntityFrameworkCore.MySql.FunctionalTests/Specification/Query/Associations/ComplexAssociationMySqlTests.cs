using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Xunit.Abstractions;
using ComplexJson = Microsoft.EntityFrameworkCore.Query.Associations.ComplexJson;
using ComplexTable = Microsoft.EntityFrameworkCore.Query.Associations.ComplexTableSplitting;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query.Associations;

/// <summary>
/// Shares one seeded JSON association model across the six official query perspectives.
/// </summary>
public sealed class ComplexJsonMySqlFixture : ComplexJson.ComplexJsonRelationalFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class
    ComplexJsonCollectionMySqlTest : ComplexJson.ComplexJsonCollectionRelationalTestBase<ComplexJsonMySqlFixture>
{
    public ComplexJsonCollectionMySqlTest(
        ComplexJsonMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class
    ComplexJsonMiscellaneousMySqlTest : ComplexJson.ComplexJsonMiscellaneousRelationalTestBase<ComplexJsonMySqlFixture>
{
    public ComplexJsonMiscellaneousMySqlTest(
        ComplexJsonMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class
    ComplexJsonPrimitiveCollectionMySqlTest : ComplexJson.ComplexJsonPrimitiveCollectionRelationalTestBase<
    ComplexJsonMySqlFixture>
{
    public ComplexJsonPrimitiveCollectionMySqlTest(
        ComplexJsonMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class
    ComplexJsonProjectionMySqlTest : ComplexJson.ComplexJsonProjectionRelationalTestBase<ComplexJsonMySqlFixture>
{
    public ComplexJsonProjectionMySqlTest(
        ComplexJsonMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class
    ComplexJsonSetOperationsMySqlTest : ComplexJson.ComplexJsonSetOperationsRelationalTestBase<ComplexJsonMySqlFixture>
{
    public ComplexJsonSetOperationsMySqlTest(
        ComplexJsonMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class
    ComplexJsonStructuralEqualityMySqlTest : ComplexJson.ComplexJsonStructuralEqualityRelationalTestBase<
    ComplexJsonMySqlFixture>
{
    public ComplexJsonStructuralEqualityMySqlTest(
        ComplexJsonMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}

/// <summary>
/// Shares one table-split complex-property model across the official association contracts.
/// </summary>
public sealed class ComplexTableSplittingMySqlFixture : ComplexTable.ComplexTableSplittingRelationalFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class ComplexTableSplittingMiscellaneousMySqlTest : ComplexTable.
    ComplexTableSplittingMiscellaneousRelationalTestBase<ComplexTableSplittingMySqlFixture>
{
    public ComplexTableSplittingMiscellaneousMySqlTest(
        ComplexTableSplittingMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class ComplexTableSplittingPrimitiveCollectionMySqlTest : ComplexTable.
    ComplexTableSplittingPrimitiveCollectionRelationalTestBase<ComplexTableSplittingMySqlFixture>
{
    public ComplexTableSplittingPrimitiveCollectionMySqlTest(
        ComplexTableSplittingMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class ComplexTableSplittingProjectionMySqlTest : ComplexTable.
    ComplexTableSplittingProjectionRelationalTestBase<ComplexTableSplittingMySqlFixture>
{
    public ComplexTableSplittingProjectionMySqlTest(
        ComplexTableSplittingMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class ComplexTableSplittingStructuralEqualityMySqlTest : ComplexTable.
    ComplexTableSplittingStructuralEqualityRelationalTestBase<ComplexTableSplittingMySqlFixture>
{
    public ComplexTableSplittingStructuralEqualityMySqlTest(
        ComplexTableSplittingMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}
