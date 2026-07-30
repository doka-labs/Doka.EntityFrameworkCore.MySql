using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Xunit.Abstractions;
using OwnedJson = Microsoft.EntityFrameworkCore.Query.Associations.OwnedJson;
using OwnedNavigations = Microsoft.EntityFrameworkCore.Query.Associations.OwnedNavigations;
using OwnedTable = Microsoft.EntityFrameworkCore.Query.Associations.OwnedTableSplitting;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query.Associations;

/// <summary>
/// Shares the JSON-owned association model across its official query contracts.
/// </summary>
public sealed class OwnedJsonMySqlFixture : OwnedJson.OwnedJsonRelationalFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class
    OwnedJsonCollectionMySqlTest : OwnedJson.OwnedJsonCollectionRelationalTestBase<OwnedJsonMySqlFixture>
{
    public OwnedJsonCollectionMySqlTest(
        OwnedJsonMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class
    OwnedJsonMiscellaneousMySqlTest : OwnedJson.OwnedJsonMiscellaneousRelationalTestBase<OwnedJsonMySqlFixture>
{
    public OwnedJsonMiscellaneousMySqlTest(
        OwnedJsonMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class
    OwnedJsonPrimitiveCollectionMySqlTest : OwnedJson.OwnedJsonPrimitiveCollectionRelationalTestBase<
    OwnedJsonMySqlFixture>
{
    public OwnedJsonPrimitiveCollectionMySqlTest(
        OwnedJsonMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class
    OwnedJsonProjectionMySqlTest : OwnedJson.OwnedJsonProjectionRelationalTestBase<OwnedJsonMySqlFixture>
{
    public OwnedJsonProjectionMySqlTest(
        OwnedJsonMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class
    OwnedJsonStructuralEqualityMySqlTest : OwnedJson.OwnedJsonStructuralEqualityRelationalTestBase<
    OwnedJsonMySqlFixture>
{
    public OwnedJsonStructuralEqualityMySqlTest(
        OwnedJsonMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}

/// <summary>
/// Shares the separately stored owned-navigation model across its query contracts.
/// </summary>
public sealed class OwnedNavigationsMySqlFixture : OwnedNavigations.OwnedNavigationsRelationalFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class
    OwnedNavigationsCollectionMySqlTest : OwnedNavigations.OwnedNavigationsCollectionRelationalTestBase<
    OwnedNavigationsMySqlFixture>
{
    public OwnedNavigationsCollectionMySqlTest(
        OwnedNavigationsMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class OwnedNavigationsMiscellaneousMySqlTest : OwnedNavigations.
    OwnedNavigationsMiscellaneousRelationalTestBase<OwnedNavigationsMySqlFixture>
{
    public OwnedNavigationsMiscellaneousMySqlTest(
        OwnedNavigationsMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class OwnedNavigationsPrimitiveCollectionMySqlTest : OwnedNavigations.
    OwnedNavigationsPrimitiveCollectionRelationalTestBase<OwnedNavigationsMySqlFixture>
{
    public OwnedNavigationsPrimitiveCollectionMySqlTest(
        OwnedNavigationsMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class
    OwnedNavigationsProjectionMySqlTest : OwnedNavigations.OwnedNavigationsProjectionRelationalTestBase<
    OwnedNavigationsMySqlFixture>
{
    public OwnedNavigationsProjectionMySqlTest(
        OwnedNavigationsMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class OwnedNavigationsSetOperationsMySqlTest : OwnedNavigations.
    OwnedNavigationsSetOperationsRelationalTestBase<OwnedNavigationsMySqlFixture>
{
    public OwnedNavigationsSetOperationsMySqlTest(
        OwnedNavigationsMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class OwnedNavigationsStructuralEqualityMySqlTest : OwnedNavigations.
    OwnedNavigationsStructuralEqualityRelationalTestBase<OwnedNavigationsMySqlFixture>
{
    public OwnedNavigationsStructuralEqualityMySqlTest(
        OwnedNavigationsMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}

/// <summary>
/// Shares the table-split owned-navigation model across its query contracts.
/// </summary>
public sealed class OwnedTableSplittingMySqlFixture : OwnedTable.OwnedTableSplittingRelationalFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class
    OwnedTableSplittingMiscellaneousMySqlTest : OwnedTable.OwnedTableSplittingMiscellaneousRelationalTestBase<
    OwnedTableSplittingMySqlFixture>
{
    public OwnedTableSplittingMiscellaneousMySqlTest(
        OwnedTableSplittingMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class OwnedTableSplittingPrimitiveCollectionMySqlTest : OwnedTable.
    OwnedTableSplittingPrimitiveCollectionRelationalTestBase<OwnedTableSplittingMySqlFixture>
{
    public OwnedTableSplittingPrimitiveCollectionMySqlTest(
        OwnedTableSplittingMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class
    OwnedTableSplittingProjectionMySqlTest : OwnedTable.OwnedTableSplittingProjectionRelationalTestBase<
    OwnedTableSplittingMySqlFixture>
{
    public OwnedTableSplittingProjectionMySqlTest(
        OwnedTableSplittingMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class OwnedTableSplittingStructuralEqualityMySqlTest : OwnedTable.
    OwnedTableSplittingStructuralEqualityRelationalTestBase<OwnedTableSplittingMySqlFixture>
{
    public OwnedTableSplittingStructuralEqualityMySqlTest(
        OwnedTableSplittingMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}
