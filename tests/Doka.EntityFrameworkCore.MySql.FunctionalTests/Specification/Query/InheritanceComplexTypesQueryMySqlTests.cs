using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query;

public sealed class TpcInheritanceJsonQueryMySqlFixture
    : TPCInheritanceJsonQueryRelationalFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

    public override bool UseGeneratedKeys => false;
}

public sealed class TphInheritanceJsonQueryMySqlFixture
    : TPHInheritanceJsonQueryRelationalFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

public sealed class TptInheritanceJsonQueryMySqlFixture
    : TPTInheritanceJsonQueryRelationalFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class TpcInheritanceJsonQueryMySqlTest
    : TPCInheritanceJsonQueryRelationalTestBase<TpcInheritanceJsonQueryMySqlFixture>
{
    public TpcInheritanceJsonQueryMySqlTest(
        TpcInheritanceJsonQueryMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class TphInheritanceJsonQueryMySqlTest
    : TPHInheritanceJsonQueryRelationalTestBase<TphInheritanceJsonQueryMySqlFixture>
{
    public TphInheritanceJsonQueryMySqlTest(
        TphInheritanceJsonQueryMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class TptInheritanceJsonQueryMySqlTest
    : TPTInheritanceJsonQueryRelationalTestBase<TptInheritanceJsonQueryMySqlFixture>
{
    public TptInheritanceJsonQueryMySqlTest(
        TptInheritanceJsonQueryMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class TpcInheritanceTableSplittingQueryMySqlTest
    : TPCInheritanceTableSplittingQueryRelationalTestBase<TpcInheritanceQueryMySqlFixture>
{
    public TpcInheritanceTableSplittingQueryMySqlTest(
        TpcInheritanceQueryMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class TphInheritanceTableSplittingQueryMySqlTest
    : TPHInheritanceTableSplittingQueryRelationalTestBase<TphInheritanceQueryMySqlFixture>
{
    public TphInheritanceTableSplittingQueryMySqlTest(
        TphInheritanceQueryMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class TptInheritanceTableSplittingQueryMySqlTest
    : TPTInheritanceTableSplittingQueryRelationalTestBase<TptInheritanceQueryMySqlFixture>
{
    public TptInheritanceTableSplittingQueryMySqlTest(
        TptInheritanceQueryMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}
