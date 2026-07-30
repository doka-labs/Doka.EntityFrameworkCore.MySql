using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.CrossCutting.NonShared;

/// <summary>
/// Exercises ad-hoc many-to-many models whose shape is built independently per test.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class AdHocManyToManyQueryMySqlTest : AdHocManyToManyQueryRelationalTestBase
{
    public AdHocManyToManyQueryMySqlTest(
        NonSharedFixture fixture
    ) : base(fixture) { }

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

/// <summary>
/// Runs relational owned-entity queries over independently constructed models.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class OwnedEntityQueryMySqlTest : OwnedEntityQueryRelationalTestBase
{
    public OwnedEntityQueryMySqlTest(
        NonSharedFixture fixture
    ) : base(fixture) { }

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

/// <summary>
/// Runs shared-type entity queries over independently constructed relational models.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class SharedTypeQueryMySqlTest : SharedTypeQueryRelationalTestBase
{
    public SharedTypeQueryMySqlTest(
        NonSharedFixture fixture
    ) : base(fixture) { }

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}
