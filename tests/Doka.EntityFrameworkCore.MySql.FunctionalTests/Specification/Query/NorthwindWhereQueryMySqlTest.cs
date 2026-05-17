using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query;

/// <summary>
/// First Northwind specification subclass. The Where variant ships first because it exercises
/// the largest cross-section of the query pipeline per test method; the remaining Northwind
/// variants (Aggregate, GroupBy, Join, ...) follow incrementally on the same fixture surface.
/// </summary>
[Trait("Category", "Spec")]
public class NorthwindWhereQueryMySqlTest : NorthwindWhereQueryRelationalTestBase<
    NorthwindQueryMySqlFixture<NoopModelCustomizer>>
{
    private const string LimitInSubqueryNotSupportedReason =
        "Both MySQL 8.4 and MariaDB 11.8 structurally reject 'LIMIT & IN/ALL/ANY/SOME "
        + "subquery' (ERROR 1235, SQLSTATE 42000). Documented in the MySQL 8.4 Reference "
        + "Manual 'Subquery Restrictions' and the MariaDB Server Reference 'Subquery "
        + "Limitations'; confirmed via direct empirical probe against both engines. "
        + "See ADR D-011 and SkipList.md Permanent skips section for the full citation.";

    public NorthwindWhereQueryMySqlTest(
        NorthwindQueryMySqlFixture<NoopModelCustomizer> fixture
    ) : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
    }

    [Theory(Skip = LimitInSubqueryNotSupportedReason)]
    [InlineData(true)]
    [InlineData(false)]
    public override Task Where_multiple_contains_in_subquery_with_or(
        bool async
    )
    {
        _ = async;
        return Task.CompletedTask;
    }

    [Theory(Skip = LimitInSubqueryNotSupportedReason)]
    [InlineData(true)]
    [InlineData(false)]
    public override Task Where_multiple_contains_in_subquery_with_and(
        bool async
    )
    {
        _ = async;
        return Task.CompletedTask;
    }

    // Anonymous-type / Tuple structural-equality comparisons (new { x = c.City } == new { x = "London" },
    // Tuple.Create(c.City) == Tuple.Create("London"), etc.) are not translated by the EF Core 10
    // RelationalSqlTranslatingExpressionVisitor — the TryRewriteStructuralTypeEquality switch covers
    // IEntityType, IComplexType, and IComplexProperty operands only, and falls through to "not translated"
    // for anonymous-type / Tuple operands. The spec-test base class captures the limitation; every
    // relational provider (SqlServer, Sqlite, PostgreSQL) overrides each test with AssertTranslationFailed
    // to document the engine-uniform behavior. Doka mirrors the same disposition for parity. See dotnet/efcore
    // issue 14672 for the upstream tracking item and the rationale for not auto-rewriting.

    public override async Task Where_compare_constructed_equal(
        bool async
    ) => await AssertTranslationFailed(() => base.Where_compare_constructed_equal(async));

    public override async Task Where_compare_constructed_multi_value_equal(
        bool async
    ) => await AssertTranslationFailed(() => base.Where_compare_constructed_multi_value_equal(async));

    public override async Task Where_compare_constructed_multi_value_not_equal(
        bool async
    ) => await AssertTranslationFailed(() => base.Where_compare_constructed_multi_value_not_equal(async));

    public override async Task Where_compare_tuple_constructed_equal(
        bool async
    ) => await AssertTranslationFailed(() => base.Where_compare_tuple_constructed_equal(async));

    public override async Task Where_compare_tuple_constructed_multi_value_equal(
        bool async
    ) => await AssertTranslationFailed(() => base.Where_compare_tuple_constructed_multi_value_equal(async));

    public override async Task Where_compare_tuple_constructed_multi_value_not_equal(
        bool async
    ) => await AssertTranslationFailed(() => base.Where_compare_tuple_constructed_multi_value_not_equal(async));

    public override async Task Where_compare_tuple_create_constructed_equal(
        bool async
    ) => await AssertTranslationFailed(() => base.Where_compare_tuple_create_constructed_equal(async));

    public override async Task Where_compare_tuple_create_constructed_multi_value_equal(
        bool async
    ) => await AssertTranslationFailed(() => base.Where_compare_tuple_create_constructed_multi_value_equal(async));

    public override async Task Where_compare_tuple_create_constructed_multi_value_not_equal(
        bool async
    ) => await AssertTranslationFailed(() => base.Where_compare_tuple_create_constructed_multi_value_not_equal(async));
}
