using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Xunit.Abstractions;
using Navigations = Microsoft.EntityFrameworkCore.Query.Associations.Navigations;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query.Associations;

/// <summary>
/// Shares the navigation-association model across collection, include, projection,
/// set-operation, primitive-collection, and equality contracts.
/// </summary>
public sealed class NavigationsMySqlFixture : Navigations.NavigationsRelationalFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class
    NavigationsCollectionMySqlTest : Navigations.NavigationsCollectionRelationalTestBase<NavigationsMySqlFixture>
{
    public NavigationsCollectionMySqlTest(
        NavigationsMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class
    NavigationsIncludeMySqlTest : Navigations.NavigationsIncludeRelationalTestBase<NavigationsMySqlFixture>
{
    public NavigationsIncludeMySqlTest(
        NavigationsMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class
    NavigationsMiscellaneousMySqlTest : Navigations.NavigationsMiscellaneousRelationalTestBase<NavigationsMySqlFixture>
{
    public NavigationsMiscellaneousMySqlTest(
        NavigationsMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class
    NavigationsPrimitiveCollectionMySqlTest : Navigations.NavigationsPrimitiveCollectionRelationalTestBase<
    NavigationsMySqlFixture>
{
    public NavigationsPrimitiveCollectionMySqlTest(
        NavigationsMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class
    NavigationsProjectionMySqlTest : Navigations.NavigationsProjectionRelationalTestBase<NavigationsMySqlFixture>
{
    public NavigationsProjectionMySqlTest(
        NavigationsMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class
    NavigationsSetOperationsMySqlTest : Navigations.NavigationsSetOperationsRelationalTestBase<NavigationsMySqlFixture>
{
    public NavigationsSetOperationsMySqlTest(
        NavigationsMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class
    NavigationsStructuralEqualityMySqlTest : Navigations.NavigationsStructuralEqualityRelationalTestBase<
    NavigationsMySqlFixture>
{
    public NavigationsStructuralEqualityMySqlTest(
        NavigationsMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}
