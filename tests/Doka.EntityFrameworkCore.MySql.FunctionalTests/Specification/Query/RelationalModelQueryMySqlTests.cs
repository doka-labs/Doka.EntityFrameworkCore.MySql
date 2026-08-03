using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query.Fixtures;
using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Microsoft.EntityFrameworkCore.TestModels.NullSemanticsModel;
using Xunit.Abstractions;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query;

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class ComplexTypeQueryMySqlTest : ComplexTypeQueryRelationalTestBase<ComplexTypeQueryMySqlFixture>
{
    public ComplexTypeQueryMySqlTest(
        ComplexTypeQueryMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class CompositeKeysQueryMySqlTest : CompositeKeysQueryRelationalTestBase<CompositeKeysQueryMySqlFixture>
{
    public CompositeKeysQueryMySqlTest(
        CompositeKeysQueryMySqlFixture fixture
    ) : base(fixture) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class
    CompositeKeysSplitQueryMySqlTest : CompositeKeysSplitQueryRelationalTestBase<CompositeKeysQueryMySqlFixture>
{
    public CompositeKeysSplitQueryMySqlTest(
        CompositeKeysQueryMySqlFixture fixture
    ) : base(fixture) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class GearsOfWarQueryMySqlTest : GearsOfWarQueryRelationalTestBase<GearsOfWarQueryMySqlFixture>
{
    public GearsOfWarQueryMySqlTest(
        GearsOfWarQueryMySqlFixture fixture,
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
    /// Verifies the complete result set and the requested SQL ordering independently.
    /// </summary>
    /// <remarks>
    /// SQL does not define a relative order for equal sort keys. The upstream
    /// LINQ-to-Objects expectation instead retains an earlier ordering because its
    /// sort is stable. The engine references and full rationale are documented on
    /// the equivalent TPC contract.
    /// </remarks>
    private async Task AssertTakeThenOrderByRank(
        bool async
    )
    {
        await AssertQuery(
            async,
            TakeThenOrderByRankQuery.Create,
            elementSorter: fullName => fullName);

        Assert.Contains("ORDER BY `g0`.`Rank`", Fixture.TestSqlLoggerFactory.Sql, StringComparison.Ordinal);
    }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class GearsOfWarFromSqlQueryMySqlTest : GearsOfWarFromSqlQueryTestBase<GearsOfWarQueryMySqlFixture>
{
    public GearsOfWarFromSqlQueryMySqlTest(
        GearsOfWarQueryMySqlFixture fixture
    ) : base(fixture) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class ManyToManyQueryMySqlTest : ManyToManyQueryRelationalTestBase<ManyToManyQueryMySqlFixture>
{
    public ManyToManyQueryMySqlTest(
        ManyToManyQueryMySqlFixture fixture
    ) : base(fixture) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class
    ManyToManyNoTrackingQueryMySqlTest : ManyToManyNoTrackingQueryRelationalTestBase<ManyToManyQueryMySqlFixture>
{
    public ManyToManyNoTrackingQueryMySqlTest(
        ManyToManyQueryMySqlFixture fixture
    ) : base(fixture) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class NullSemanticsQueryMySqlTest : NullSemanticsQueryTestBase<NullSemanticsQueryMySqlFixture>
{
    public NullSemanticsQueryMySqlTest(
        NullSemanticsQueryMySqlFixture fixture
    ) : base(fixture) { }

    protected override NullSemanticsContext CreateContext(
        bool useRelationalNulls = false
    )
    {
        var optionsBuilder = new DbContextOptionsBuilder(Fixture.CreateOptions());

        if (useRelationalNulls)
        {
            new MySqlDbContextOptionsBuilder(optionsBuilder).UseRelationalNulls();
        }

        var context = new NullSemanticsContext(optionsBuilder.Options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        return context;
    }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class OptionalDependentQueryMySqlTest : OptionalDependentQueryTestBase<OptionalDependentQueryMySqlFixture>
{
    public OptionalDependentQueryMySqlTest(
        OptionalDependentQueryMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class OwnedQueryMySqlTest : OwnedQueryRelationalTestBase<OwnedQueryMySqlTest.MySqlFixture>
{
    public OwnedQueryMySqlTest(
        MySqlFixture fixture
    ) : base(fixture) { }

    public sealed class MySqlFixture : RelationalOwnedQueryFixture
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
    }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class InheritanceRelationshipsQueryMySqlTest : InheritanceRelationshipsQueryRelationalTestBase<
    InheritanceRelationshipsQueryMySqlTest.MySqlFixture>
{
    public InheritanceRelationshipsQueryMySqlTest(
        MySqlFixture fixture
    ) : base(fixture) { }

    public sealed class MySqlFixture : InheritanceRelationshipsQueryRelationalFixture
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
    }
}
