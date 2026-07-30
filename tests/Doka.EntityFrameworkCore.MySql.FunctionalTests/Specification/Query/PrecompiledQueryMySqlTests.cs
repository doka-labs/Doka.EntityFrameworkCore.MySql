using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Xunit.Abstractions;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query;

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class AdHocPrecompiledQueryMySqlTest : AdHocPrecompiledQueryRelationalTestBase
{
    public AdHocPrecompiledQueryMySqlTest(
        NonSharedFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }

    protected override bool AlwaysPrintGeneratedSources => false;

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

    protected override PrecompiledQueryTestHelpers PrecompiledQueryTestHelpers =>
        MySqlPrecompiledQueryTestHelpers.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class PrecompiledQueryMySqlTest : PrecompiledQueryRelationalTestBase,
    IClassFixture<PrecompiledQueryMySqlFixture>
{
    public PrecompiledQueryMySqlTest(
        PrecompiledQueryMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }

    protected override bool AlwaysPrintGeneratedSources => false;
}

public sealed class PrecompiledQueryMySqlFixture : PrecompiledQueryRelationalFixture
{
    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

    public override PrecompiledQueryTestHelpers PrecompiledQueryTestHelpers =>
        MySqlPrecompiledQueryTestHelpers.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class PrecompiledSqlPregenerationQueryMySqlTest : PrecompiledSqlPregenerationQueryRelationalTestBase,
    IClassFixture<PrecompiledSqlPregenerationQueryMySqlFixture>
{
    public PrecompiledSqlPregenerationQueryMySqlTest(
        PrecompiledSqlPregenerationQueryMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }

    protected override bool AlwaysPrintGeneratedSources => false;
}

public sealed class PrecompiledSqlPregenerationQueryMySqlFixture : PrecompiledSqlPregenerationQueryRelationalFixture
{
    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

    public override PrecompiledQueryTestHelpers PrecompiledQueryTestHelpers =>
        MySqlPrecompiledQueryTestHelpers.Instance;
}
