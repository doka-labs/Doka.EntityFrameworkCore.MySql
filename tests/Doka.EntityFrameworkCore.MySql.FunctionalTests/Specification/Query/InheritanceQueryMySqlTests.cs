using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Microsoft.EntityFrameworkCore.TestModels.GearsOfWarModel;
using Xunit.Abstractions;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query;

public class TpcInheritanceQueryMySqlFixture : TPCInheritanceQueryFixture
{
    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

    public override bool UseGeneratedKeys => false;
}

public sealed class TpcFiltersInheritanceQueryMySqlFixture : TpcInheritanceQueryMySqlFixture
{
    public override bool EnableFilters => true;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class TpcInheritanceQueryMySqlTest : TPCInheritanceQueryTestBase<TpcInheritanceQueryMySqlFixture>
{
    public TpcInheritanceQueryMySqlTest(
        TpcInheritanceQueryMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }

    protected override void UseTransaction(
        DatabaseFacade facade,
        IDbContextTransaction transaction
    ) => facade.UseTransaction(transaction.GetDbTransaction());
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class
    TpcFiltersInheritanceQueryMySqlTest : TPCFiltersInheritanceQueryTestBase<TpcFiltersInheritanceQueryMySqlFixture>
{
    public TpcFiltersInheritanceQueryMySqlTest(
        TpcFiltersInheritanceQueryMySqlFixture fixture
    ) : base(fixture) { }
}

public class TphInheritanceQueryMySqlFixture : TPHInheritanceQueryFixture
{
    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

public sealed class TphFiltersInheritanceQueryMySqlFixture : TphInheritanceQueryMySqlFixture
{
    public override bool EnableFilters => true;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class TphInheritanceQueryMySqlTest : TPHInheritanceQueryTestBase<TphInheritanceQueryMySqlFixture>
{
    public TphInheritanceQueryMySqlTest(
        TphInheritanceQueryMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class
    TphFiltersInheritanceQueryMySqlTest : FiltersInheritanceQueryTestBase<TphFiltersInheritanceQueryMySqlFixture>
{
    public TphFiltersInheritanceQueryMySqlTest(
        TphFiltersInheritanceQueryMySqlFixture fixture
    ) : base(fixture) { }
}

public class TptInheritanceQueryMySqlFixture : TPTInheritanceQueryFixture
{
    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

public sealed class TptFiltersInheritanceQueryMySqlFixture : TptInheritanceQueryMySqlFixture
{
    public override bool EnableFilters => true;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class TptInheritanceQueryMySqlTest : TPTInheritanceQueryTestBase<TptInheritanceQueryMySqlFixture>
{
    public TptInheritanceQueryMySqlTest(
        TptInheritanceQueryMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture, testOutputHelper) { }

    protected override void UseTransaction(
        DatabaseFacade facade,
        IDbContextTransaction transaction
    ) => facade.UseTransaction(transaction.GetDbTransaction());
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class
    TptFiltersInheritanceQueryMySqlTest : TPTFiltersInheritanceQueryTestBase<TptFiltersInheritanceQueryMySqlFixture>
{
    public TptFiltersInheritanceQueryMySqlTest(
        TptFiltersInheritanceQueryMySqlFixture fixture
    ) : base(fixture) { }
}

public sealed class TpcManyToManyQueryMySqlFixture : TPCManyToManyQueryRelationalFixture
{
    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class
    TpcManyToManyQueryMySqlTest : TPCManyToManyQueryRelationalTestBase<TpcManyToManyQueryMySqlFixture>
{
    public TpcManyToManyQueryMySqlTest(
        TpcManyToManyQueryMySqlFixture fixture
    ) : base(fixture) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class
    TpcManyToManyNoTrackingQueryMySqlTest : TPCManyToManyNoTrackingQueryRelationalTestBase<
    TpcManyToManyQueryMySqlFixture>
{
    public TpcManyToManyNoTrackingQueryMySqlTest(
        TpcManyToManyQueryMySqlFixture fixture
    ) : base(fixture) { }
}

public sealed class TptManyToManyQueryMySqlFixture : TPTManyToManyQueryRelationalFixture
{
    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class
    TptManyToManyQueryMySqlTest : TPTManyToManyQueryRelationalTestBase<TptManyToManyQueryMySqlFixture>
{
    public TptManyToManyQueryMySqlTest(
        TptManyToManyQueryMySqlFixture fixture
    ) : base(fixture) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class
    TptManyToManyNoTrackingQueryMySqlTest : TPTManyToManyNoTrackingQueryRelationalTestBase<
    TptManyToManyQueryMySqlFixture>
{
    public TptManyToManyNoTrackingQueryMySqlTest(
        TptManyToManyQueryMySqlFixture fixture
    ) : base(fixture) { }
}

public sealed class TpcRelationshipsQueryMySqlFixture : TPCRelationshipsQueryRelationalFixture
{
    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class TpcRelationshipsQueryMySqlTest : TPCRelationshipsQueryTestBase<TpcRelationshipsQueryMySqlFixture>
{
    public TpcRelationshipsQueryMySqlTest(
        TpcRelationshipsQueryMySqlFixture fixture
    ) : base(fixture) { }
}

public sealed class TptRelationshipsQueryMySqlFixture : TPTRelationshipsQueryRelationalFixture
{
    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class TptRelationshipsQueryMySqlTest : TPTRelationshipsQueryTestBase<TptRelationshipsQueryMySqlFixture>
{
    public TptRelationshipsQueryMySqlTest(
        TptRelationshipsQueryMySqlFixture fixture
    ) : base(fixture) { }
}

public sealed class TpcGearsOfWarQueryMySqlFixture : TPCGearsOfWarQueryRelationalFixture
{
    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class
    TpcGearsOfWarQueryMySqlTest : TPCGearsOfWarQueryRelationalTestBase<TpcGearsOfWarQueryMySqlFixture>
{
    public TpcGearsOfWarQueryMySqlTest(
        TpcGearsOfWarQueryMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    /// <summary>
    /// Executes the upstream take-then-order contract without assigning a deterministic
    /// relative order to rows whose requested <c>Rank</c> values are equal.
    /// </summary>
    [DirectTheory]
    [InheritedTheoryData]
    public override Task Take_without_orderby_followed_by_orderBy_is_pushed_down1(
        bool async
    ) => AssertTakeThenOrderByRank(async);

    /// <summary>
    /// Executes the equivalent query-syntax contract without assigning a deterministic
    /// relative order to rows whose requested <c>Rank</c> values are equal.
    /// </summary>
    [DirectTheory]
    [InheritedTheoryData]
    public override Task Take_without_orderby_followed_by_orderBy_is_pushed_down2(
        bool async
    ) => AssertTakeThenOrderByRank(async);

    [SpecEngineLimitationTheory("MYSQL-MARIADB-TEMPORAL-MICROSECOND-PRECISION", "mysql84", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Non_string_concat_uses_appropriate_type_mapping(
        bool async
    ) => base.Non_string_concat_uses_appropriate_type_mapping(async);

    /// <summary>
    /// Verifies the complete result set and the requested SQL ordering independently,
    /// because SQL does not define the order of rows whose <c>Rank</c> values are equal.
    /// </summary>
    /// <remarks>
    /// The upstream LINQ-to-Objects expectation uses a stable sort and therefore retains
    /// the earlier <c>FullName</c> order for equal ranks. The translated SQL correctly
    /// replaces that ordering with <c>ORDER BY Rank</c>. MySQL documents that equal sort
    /// keys may be returned in any order, while MariaDB documents that another ordering
    /// expression is required to order ties. Sources retrieved 2026-07-30:
    /// <see href="https://dev.mysql.com/doc/refman/8.4/en/limit-optimization.html">
    /// MySQL LIMIT query optimization</see> and
    /// <see href="https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/order-by">
    /// MariaDB ORDER BY</see>.
    /// </remarks>
    private async Task AssertTakeThenOrderByRank(
        bool async
    )
    {
        await AssertQuery(
            async,
            TakeThenOrderByRankQuery.Create,
            elementSorter: fullName => fullName);

        Assert.Contains("ORDER BY `u0`.`Rank`", Fixture.TestSqlLoggerFactory.Sql, StringComparison.Ordinal);
    }
}

public sealed class TptGearsOfWarQueryMySqlFixture : TPTGearsOfWarQueryRelationalFixture
{
    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class
    TptGearsOfWarQueryMySqlTest : TPTGearsOfWarQueryRelationalTestBase<TptGearsOfWarQueryMySqlFixture>
{
    public TptGearsOfWarQueryMySqlTest(
        TptGearsOfWarQueryMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    /// <summary>
    /// Executes the upstream take-then-order contract without imposing a relative
    /// order on rows whose requested <c>Rank</c> values are equal.
    /// </summary>
    [DirectTheory]
    [InheritedTheoryData]
    public override Task Take_without_orderby_followed_by_orderBy_is_pushed_down1(
        bool async
    ) => AssertTakeThenOrderByRank(async);

    /// <summary>
    /// Executes the equivalent query-syntax contract without imposing a relative
    /// order on rows whose requested <c>Rank</c> values are equal.
    /// </summary>
    [DirectTheory]
    [InheritedTheoryData]
    public override Task Take_without_orderby_followed_by_orderBy_is_pushed_down2(
        bool async
    ) => AssertTakeThenOrderByRank(async);

    [SpecEngineLimitationTheory("MYSQL-MARIADB-TEMPORAL-MICROSECOND-PRECISION", "mysql84", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Non_string_concat_uses_appropriate_type_mapping(
        bool async
    ) => base.Non_string_concat_uses_appropriate_type_mapping(async);

    /// <summary>
    /// Verifies the complete result set and the requested SQL ordering independently.
    /// </summary>
    /// <remarks>
    /// SQL does not define a relative order for equal sort keys. The upstream
    /// LINQ-to-Objects expectation instead retains an earlier ordering because its
    /// sort is stable. The engine references and rationale are documented on the
    /// equivalent TPC contract above.
    /// </remarks>
    private async Task AssertTakeThenOrderByRank(
        bool async
    )
    {
        await AssertQuery(async, TakeThenOrderByRankQuery.Create, elementSorter: fullName => fullName);

        Assert.Contains("ORDER BY `s`.`Rank`", Fixture.TestSqlLoggerFactory.Sql, StringComparison.Ordinal);
    }
}

/// <summary>
/// Provides the shared query shape for inheritance strategies that verify take
/// pushdown followed by replacement ordering.
/// </summary>
internal static class TakeThenOrderByRankQuery
{
    /// <summary>
    /// Builds the query shared by the TPC and TPT conformance contracts.
    /// </summary>
    public static IQueryable<string> Create(
        ISetSource source
    ) => source
        .Set<Gear>()
        .Where(gear => !gear.HasSoulPatch)
        .Take(999)
        .OrderBy(gear => gear.FullName)
        // The second OrderBy intentionally replaces the preceding ordering. ThenBy
        // would retain FullName as the primary key and change this conformance contract.
        .OrderBy(gear => gear.Rank)
        .Select(gear => gear.FullName);
}
