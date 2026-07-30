using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Microsoft.EntityFrameworkCore.TestModels.Northwind;
using Xunit.Abstractions;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query;

/// <summary>
/// Executes the official relational aggregate-operator contract on the provider.
/// </summary>
/// <remarks>
/// MySQL documents approximate floating-point values as implementation-dependent and
/// recommends tolerance-based comparisons. Source retrieved 2026-07-28:
/// <see href="https://dev.mysql.com/doc/refman/8.4/en/problems-with-float.html">
/// Problems with Floating-Point Values</see>.
/// </remarks>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public class NorthwindAggregateOperatorsQueryMySqlTest : NorthwindAggregateOperatorsQueryRelationalTestBase<
    NorthwindQueryMySqlFixture<NoopModelCustomizer>>
{
    private const decimal DecimalAggregateTolerance = 0.000000000001m;
    private const decimal DecimalFromFloatAggregateTolerance = 0.00001m;
    private const float FloatAggregateTolerance = 0.0000001f;

    public NorthwindAggregateOperatorsQueryMySqlTest(
        NorthwindQueryMySqlFixture<NoopModelCustomizer> fixture
    ) : base(fixture) { }

    // MariaDB cannot represent these correlated derived-table shapes because its
    // JOIN grammar has no LATERAL form. Discovery links the executable skips to
    // the primary-source-backed disposition ledger.

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Multiple_collection_navigation_with_FirstOrDefault_chained(
        bool async
    ) => base.Multiple_collection_navigation_with_FirstOrDefault_chained(async);

    /// <summary>
    /// Verifies the active EF Core translation boundary for parameterized
    /// collections of composite anonymous values.
    /// </summary>
    /// <remarks>
    /// EF Core issue 14672 tracks decomposition of anonymous and tuple values.
    /// Source retrieved 2026-07-28:
    /// <see href="https://github.com/dotnet/efcore/issues/14672">dotnet/efcore#14672</see>.
    /// </remarks>
    public override Task Contains_with_local_anonymous_type_array_closure(
        bool async
    ) => AssertTranslationFailed(() => base.Contains_with_local_anonymous_type_array_closure(async));

    /// <summary>
    /// Verifies the active EF Core translation boundary for parameterized
    /// collections of composite tuple values.
    /// </summary>
    /// <remarks>
    /// EF Core issue 14672 tracks decomposition of anonymous and tuple values.
    /// Source retrieved 2026-07-28:
    /// <see href="https://github.com/dotnet/efcore/issues/14672">dotnet/efcore#14672</see>.
    /// </remarks>
    public override Task Contains_with_local_tuple_array_closure(
        bool async
    ) => AssertTranslationFailed(() => base.Contains_with_local_tuple_array_closure(async));

    /// <summary>
    /// Compares the maximally precise engine result with the CLR aggregate using a
    /// sub-picounit tolerance for their different floating-point evaluation order.
    /// </summary>
    public override Task Average_over_nested_subquery(
        bool async
    ) => AssertAverage(
        async,
        ss => ss
            .Set<Customer>()
            .OrderBy(customer => customer.CustomerID)
            .Take(3),
        selector: customer =>
            (decimal)customer.Orders.Average(double (order) =>
                5 + order.OrderDetails.Average(int (detail) => detail.ProductID)),
        asserter: (
            expected,
            actual
        ) => Assert.InRange(expected - actual, -DecimalAggregateTolerance, DecimalAggregateTolerance));

    /// <summary>
    /// Compares the maximally precise engine result with the CLR aggregate using a
    /// sub-picounit tolerance for their different floating-point evaluation order.
    /// </summary>
    public override Task Average_over_max_subquery(
        bool async
    ) => AssertAverage(
        async,
        ss => ss
            .Set<Customer>()
            .OrderBy(customer => customer.CustomerID)
            .Take(3),
        selector: customer =>
            (decimal)customer.Orders.Average(int (order) =>
                5 + order.OrderDetails.Max(int (detail) => detail.ProductID)),
        asserter: (
            expected,
            actual
        ) => Assert.InRange(expected - actual, -DecimalAggregateTolerance, DecimalAggregateTolerance));

    /// <summary>
    /// Uses the engine vendor's recommended tolerance instead of exact equality for
    /// approximate FLOAT aggregation.
    /// </summary>
    public override Task Average_on_float_column(
        bool async
    ) => AssertAverage(
        async,
        ss => ss
            .Set<OrderDetail>()
            .Where(detail => detail.ProductID == 1),
        selector: detail => detail.Discount,
        asserter: AssertFloatAggregate);

    /// <summary>
    /// Uses the engine vendor's recommended tolerance instead of exact equality for
    /// approximate FLOAT aggregation inside a projection.
    /// </summary>
    public override Task Average_on_float_column_in_subquery(
        bool async
    ) => AssertQuery(
        async,
        ss => ss
            .Set<Order>()
            .Where(order => order.OrderID < 10300)
            .Select(order => new
            {
                order.OrderID,
                Sum = order.OrderDetails.Average(detail => detail.Discount),
            }),
        elementSorter: element => element.OrderID,
        elementAsserter: (
            expected,
            actual
        ) =>
        {
            Assert.Equal(expected.OrderID, actual.OrderID);
            AssertFloatAggregate(expected.Sum, actual.Sum);
        });

    /// <summary>
    /// Uses the engine vendor's recommended tolerance instead of exact equality for
    /// nullable approximate FLOAT aggregation inside a projection.
    /// </summary>
    public override Task Average_on_float_column_in_subquery_with_cast(
        bool async
    ) => AssertQuery(
        async,
        ss => ss
            .Set<Order>()
            .Where(order => order.OrderID < 10300)
            .Select(order => new
            {
                order.OrderID,
                Sum = order.OrderDetails.Average(detail => (float?)detail.Discount),
            }),
        elementSorter: element => element.OrderID,
        elementAsserter: (
            expected,
            actual
        ) =>
        {
            Assert.Equal(expected.OrderID, actual.OrderID);
            Assert.Equal(expected.Sum.HasValue, actual.Sum.HasValue);

            if (expected.Sum is { } expectedSum
                && actual.Sum is { } actualSum)
            {
                AssertFloatAggregate(expectedSum, actualSum);
            }
        });

    /// <summary>
    /// Uses the engine vendor's recommended tolerance when a binary FLOAT is
    /// converted to DECIMAL before aggregation.
    /// </summary>
    public override Task Type_casting_inside_sum(
        bool async
    ) => AssertSum(
        async,
        ss => ss.Set<OrderDetail>(),
        selector: detail => (decimal)detail.Discount,
        asserter: (
            expected,
            actual
        ) => Assert.InRange(
            expected - actual,
            -DecimalFromFloatAggregateTolerance,
            DecimalFromFloatAggregateTolerance));

    private static void AssertFloatAggregate(
        float expected,
        float actual
    ) => Assert.InRange(expected - actual, -FloatAggregateTolerance, FloatAggregateTolerance);
}

/// <summary>
/// Executes the official relational database-function contract on the provider.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public class NorthwindDbFunctionsQueryMySqlTest : NorthwindDbFunctionsQueryRelationalTestBase<
    NorthwindQueryMySqlFixture<NoopModelCustomizer>>
{
    public NorthwindDbFunctionsQueryMySqlTest(
        NorthwindQueryMySqlFixture<NoopModelCustomizer> fixture
    ) : base(fixture) { }

    protected override string CaseSensitiveCollation => "utf8mb4_bin";

    protected override string CaseInsensitiveCollation => "utf8mb4_unicode_ci";
}

/// <summary>
/// Executes the official relational function contract on the provider.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public class
    NorthwindFunctionsQueryMySqlTest : NorthwindFunctionsQueryRelationalTestBase<
    NorthwindQueryMySqlFixture<NoopModelCustomizer>>
{
    public NorthwindFunctionsQueryMySqlTest(
        NorthwindQueryMySqlFixture<NoopModelCustomizer> fixture
    ) : base(fixture) { }
}

/// <summary>
/// Executes the official relational grouping contract on the provider.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public class
    NorthwindGroupByQueryMySqlTest : NorthwindGroupByQueryRelationalTestBase<
    NorthwindQueryMySqlFixture<NoopModelCustomizer>>
{
    public NorthwindGroupByQueryMySqlTest(
        NorthwindQueryMySqlFixture<NoopModelCustomizer> fixture
    ) : base(fixture) { }

    // These grouping projections require per-outer-row derived-table evaluation,
    // which MariaDB cannot express without a LATERAL join.

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task AsEnumerable_in_subquery_for_GroupBy(
        bool async
    ) => base.AsEnumerable_in_subquery_for_GroupBy(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Complex_query_with_groupBy_in_subquery1(
        bool async
    ) => base.Complex_query_with_groupBy_in_subquery1(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Complex_query_with_groupBy_in_subquery2(
        bool async
    ) => base.Complex_query_with_groupBy_in_subquery2(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Complex_query_with_groupBy_in_subquery3(
        bool async
    ) => base.Complex_query_with_groupBy_in_subquery3(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Complex_query_with_groupBy_in_subquery4(
        bool async
    ) => base.Complex_query_with_groupBy_in_subquery4(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task GroupBy_Count_in_projection(
        bool async
    ) => base.GroupBy_Count_in_projection(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Select_nested_collection_with_groupby(
        bool async
    ) => base.Select_nested_collection_with_groupby(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Select_uncorrelated_collection_with_groupby_multiple_collections_work(
        bool async
    ) => base.Select_uncorrelated_collection_with_groupby_multiple_collections_work(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Select_uncorrelated_collection_with_groupby_when_outer_is_distinct(
        bool async
    ) => base.Select_uncorrelated_collection_with_groupby_when_outer_is_distinct(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Select_uncorrelated_collection_with_groupby_works(
        bool async
    ) => base.Select_uncorrelated_collection_with_groupby_works(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task GroupBy_aggregate_left_join_GroupBy_aggregate_left_join(
        bool async
    ) => base.GroupBy_aggregate_left_join_GroupBy_aggregate_left_join(async);

    // EF Core still produces incorrect grouping semantics for these shapes before
    // provider SQL generation can reconstruct the lost query intent.

    [SpecFrameworkLimitationTheory("EFCORE-29014")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task GroupBy_with_group_key_being_navigation_with_complex_projection(
        bool async
    ) => base.GroupBy_with_group_key_being_navigation_with_complex_projection(async);

    [SpecFrameworkLimitationTheory("EFCORE-27130")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task GroupBy_aggregate_from_multiple_query_in_same_projection(
        bool async
    ) => base.GroupBy_aggregate_from_multiple_query_in_same_projection(async);

    [SpecFrameworkLimitationTheory("EFCORE-27130")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task GroupBy_aggregate_from_multiple_query_in_same_projection_3(
        bool async
    ) => base.GroupBy_aggregate_from_multiple_query_in_same_projection_3(async);
}

/// <summary>
/// Executes the official relational join contract on the provider.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public class
    NorthwindJoinQueryMySqlTest : NorthwindJoinQueryRelationalTestBase<NorthwindQueryMySqlFixture<NoopModelCustomizer>>
{
    public NorthwindJoinQueryMySqlTest(
        NorthwindQueryMySqlFixture<NoopModelCustomizer> fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    // These collection shapers retain an outer reference across a derived-table
    // boundary and therefore need LATERAL on MariaDB.

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task SelectMany_with_client_eval_with_collection_shaper(
        bool async
    ) => base.SelectMany_with_client_eval_with_collection_shaper(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task SelectMany_with_selecting_outer_element(
        bool async
    ) => base.SelectMany_with_selecting_outer_element(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task SelectMany_with_selecting_outer_entity_column_and_inner_column(
        bool async
    ) => base.SelectMany_with_selecting_outer_entity_column_and_inner_column(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Take_in_collection_projection_with_FirstOrDefault_on_top_level(
        bool async
    ) => base.Take_in_collection_projection_with_FirstOrDefault_on_top_level(async);

    /// <summary>
    /// Executes byte-collection joins instead of preserving EF Core's upstream
    /// translation-failure expectation.
    /// </summary>
    /// <remarks>
    /// EF Core issue 30677 tracks the skipped upstream test. The provider can
    /// preserve the collection's numeric byte semantics, so this override requires
    /// successful execution. Source retrieved 2026-07-29:
    /// <see href="https://github.com/dotnet/efcore/issues/30677">dotnet/efcore#30677</see>.
    /// </remarks>
    [DirectTheory]
    [InlineData(false)]
    [InlineData(true)]
    public override async Task Join_local_bytes_closure_is_cached_correctly(
        bool async
    )
    {
        byte[] ids =
        [
            1,
            2,
        ];
        await AssertQueryScalar(
            async,
            ss => from employee in ss.Set<Employee>()
                  join id in ids on employee.EmployeeID equals id
                  select employee.EmployeeID);

        ids = [3];
        await AssertQueryScalar(
            async,
            ss => from employee in ss.Set<Employee>()
                  join id in ids on employee.EmployeeID equals id
                  select employee.EmployeeID);
    }

    /// <summary>
    /// Executes string-character joins with CLR numeric character semantics instead
    /// of treating digit characters as their decimal values.
    /// </summary>
    /// <remarks>
    /// EF Core issue 30677 tracks the skipped upstream test. The provider accepts
    /// the stronger contract of translating the enumerable string correctly.
    /// Source retrieved 2026-07-29:
    /// <see href="https://github.com/dotnet/efcore/issues/30677">dotnet/efcore#30677</see>.
    /// </remarks>
    [DirectTheory]
    [InlineData(false)]
    [InlineData(true)]
    public override async Task Join_local_string_closure_is_cached_correctly(
        bool async
    )
    {
        var ids = "12";
        await AssertQueryScalar(
            async,
            ss => from employee in ss.Set<Employee>()
                  join id in ids on employee.EmployeeID equals id
                  select employee.EmployeeID,
            assertEmpty: true);

        // Control characters prove the positive numeric conversion without
        // conflating the character value with decimal text parsing.
        ids = "\u0001\u0002";
        await AssertQueryScalar(
            async,
            ss => from employee in ss.Set<Employee>()
                  join id in ids on employee.EmployeeID equals id
                  select employee.EmployeeID);

        ids = "3";
        await AssertQueryScalar(
            async,
            ss => from employee in ss.Set<Employee>()
                  join id in ids on employee.EmployeeID equals id
                  select employee.EmployeeID,
            assertEmpty: true);
    }

    [SpecFrameworkLimitationTheory("EFCORE-35028")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Join_with_key_selectors_being_nested_anonymous_objects(
        bool async
    ) => base.Join_with_key_selectors_being_nested_anonymous_objects(async);

    [SpecFrameworkLimitationTheory("EFCORE-35028")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task GroupJoin_aggregate_nested_anonymous_key_selectors(
        bool async
    ) => base.GroupJoin_aggregate_nested_anonymous_key_selectors(async);
}

/// <summary>
/// Executes the official relational keyless-entity contract on the provider.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public class NorthwindKeylessEntitiesQueryMySqlTest : NorthwindKeylessEntitiesQueryRelationalTestBase<
    NorthwindQueryMySqlFixture<NoopModelCustomizer>>
{
    public NorthwindKeylessEntitiesQueryMySqlTest(
        NorthwindQueryMySqlFixture<NoopModelCustomizer> fixture
    ) : base(fixture) { }
}

/// <summary>
/// Executes the official relational miscellaneous-query contract on the provider.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public class NorthwindMiscellaneousQueryMySqlTest : NorthwindMiscellaneousQueryRelationalTestBase<
    NorthwindQueryMySqlFixture<NoopModelCustomizer>>
{
    public NorthwindMiscellaneousQueryMySqlTest(
        NorthwindQueryMySqlFixture<NoopModelCustomizer> fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    // These composed subqueries require a correlated derived-table boundary.
    // MariaDB supports neither correlated FROM subqueries nor LATERAL joins.

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Complex_nested_query_doesnt_try_binding_to_grandparent_when_parent_returns_complex_result(
        bool async
    ) => base.Complex_nested_query_doesnt_try_binding_to_grandparent_when_parent_returns_complex_result(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Correlated_collection_with_distinct_without_default_identifiers_projecting_columns(
        bool async
    ) => base.Correlated_collection_with_distinct_without_default_identifiers_projecting_columns(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task
        Correlated_collection_with_distinct_without_default_identifiers_projecting_columns_with_navigation(
            bool async
        ) => base.Correlated_collection_with_distinct_without_default_identifiers_projecting_columns_with_navigation(
        async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task DefaultIfEmpty_Sum_over_collection_navigation(
        bool async
    ) => base.DefaultIfEmpty_Sum_over_collection_navigation(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task SelectMany_correlated_subquery_hard(
        bool async
    ) => base.SelectMany_correlated_subquery_hard(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task SelectMany_correlated_with_Select_value_type_and_DefaultIfEmpty_in_selector(
        bool async
    ) => base.SelectMany_correlated_with_Select_value_type_and_DefaultIfEmpty_in_selector(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Select_correlated_subquery_ordered(
        bool async
    ) => base.Select_correlated_subquery_ordered(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Select_subquery_recursive_trivial(
        bool async
    ) => base.Select_subquery_recursive_trivial(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Subquery_with_navigation_inside_inline_collection(
        bool async
    ) => base.Subquery_with_navigation_inside_inline_collection(async);

    /// <summary>
    /// Requires a coalesce between an unsigned nullable column and a double
    /// fallback to preserve the promoted CLR result type.
    /// </summary>
    [DirectTheory]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Coalesce_Correct_TypeMapping_Double(
        bool async
    ) => base.Coalesce_Correct_TypeMapping_Double(async);

    /// <summary>
    /// Verifies the projected collection without assigning semantic meaning to
    /// an unordered child sequence.
    /// </summary>
    /// <remarks>
    /// The query orders its parent rows but does not order <c>OrderDetails</c>.
    /// MySQL only guarantees result order for expressions in the applicable
    /// <c>ORDER BY</c>. Source retrieved 2026-07-28:
    /// <see href="https://dev.mysql.com/doc/refman/8.4/en/select.html">SELECT Statement</see>.
    /// </remarks>
    public override Task Projection_take_collection_projection(
        bool async
    ) => AssertQuery(
        async,
        ss => ss
            .Set<Order>()
            .Where(order => order.OrderID < 10300)
            .OrderBy(order => order.OrderID)
            .Select(order => new
            {
                Item = order,
            })
            .Take(10)
            .Select(element => new
            {
                element.Item.OrderID,
                ProductIds = element
                    .Item.OrderDetails.Select(detail => detail.ProductID)
                    .ToList(),
            }),
        assertOrder: true,
        elementAsserter: (
            expected,
            actual
        ) =>
        {
            Assert.Equal(expected.OrderID, actual.OrderID);
            AssertCollection(
                expected.ProductIds,
                actual.ProductIds,
                elementSorter: productId => productId,
                elementAsserter: (
                    expectedProductId,
                    actualProductId
                ) => Assert.Equal(expectedProductId, actualProductId));
        });

    /// <summary>
    /// Verifies the paged parent projection while treating its unordered child
    /// collection as an unordered sequence of projected values.
    /// </summary>
    /// <remarks>
    /// MariaDB likewise documents that ordering inside a relational subquery is
    /// not a result-order guarantee. Source retrieved 2026-07-28:
    /// <see href="https://mariadb.com/kb/en/why-is-order-by-in-a-from-subquery-ignored/">
    /// Why is ORDER BY in a FROM Subquery Ignored?</see>.
    /// </remarks>
    public override Task Projection_skip_take_collection_projection(
        bool async
    ) => AssertQuery(
        async,
        ss => ss
            .Set<Order>()
            .Where(order => order.OrderID < 10300)
            .OrderBy(order => order.OrderID)
            .Select(order => new
            {
                Item = order,
            })
            .Skip(5)
            .Take(10)
            .Select(element => new
            {
                element.Item.OrderID,
                ProductIds = element
                    .Item.OrderDetails.Select(detail => detail.ProductID)
                    .ToList(),
            }),
        assertOrder: true,
        elementAsserter: (
            expected,
            actual
        ) =>
        {
            Assert.Equal(expected.OrderID, actual.OrderID);
            AssertCollection(
                expected.ProductIds,
                actual.ProductIds,
                elementSorter: productId => productId,
                elementAsserter: (
                    expectedProductId,
                    actualProductId
                ) => Assert.Equal(expectedProductId, actualProductId));
        });

    /// <summary>
    /// Verifies the unbounded paged parent projection without treating its unordered
    /// child collection as an ordered sequence.
    /// </summary>
    public override Task Projection_skip_collection_projection(
        bool async
    ) => AssertQuery(
        async,
        ss => ss
            .Set<Order>()
            .Where(order => order.OrderID < 10300)
            .OrderBy(order => order.OrderID)
            .Select(order => new
            {
                Item = order,
            })
            .Skip(5)
            .Select(element => new
            {
                element.Item.OrderID,
                ProductIds = element
                    .Item.OrderDetails.Select(detail => detail.ProductID)
                    .ToList(),
            }),
        assertOrder: true,
        elementAsserter: (
            expected,
            actual
        ) =>
        {
            Assert.Equal(expected.OrderID, actual.OrderID);
            AssertCollection(
                expected.ProductIds,
                actual.ProductIds,
                elementSorter: productId => productId,
                elementAsserter: (
                    expectedProductId,
                    actualProductId
                ) => Assert.Equal(expectedProductId, actualProductId));
        });

    /// <summary>
    /// Preserves entity-valued subquery ordering while adding deterministic ordering
    /// for both the selected order and customers without orders.
    /// </summary>
    public override Task Entity_equality_orderby_subquery(
        bool async
    ) => AssertQuery(
        async,
        ss => ss
            .Set<Customer>()
            .OrderBy(customer => customer
                .Orders.OrderBy(order => order.OrderID)
                .FirstOrDefault())
            .ThenBy(customer => customer.CustomerID),
        ss => ss
            .Set<Customer>()
            .OrderBy(customer => customer
                .Orders.OrderBy(order => order.OrderID)
                .Select(order => (int?)order.OrderID)
                .FirstOrDefault())
            .ThenBy(customer => customer.CustomerID),
        assertOrder: true);

    public override Task Client_code_unknown_method(
        bool async
    ) => AssertTranslationFailed(() => base.Client_code_unknown_method(async));

    public override async Task Client_code_using_instance_in_anonymous_type(
        bool async
    ) => Assert.Equal(
        CoreStrings.ClientProjectionCapturingConstantInTree(typeof(NorthwindMiscellaneousQueryMySqlTest).FullName!),
        (await Assert.ThrowsAsync<InvalidOperationException>(() =>
            base.Client_code_using_instance_in_anonymous_type(async))).Message);

    public override async Task Client_code_using_instance_in_static_method(
        bool async
    ) => Assert.Equal(
        CoreStrings.ClientProjectionCapturingConstantInMethodArgument(
            typeof(NorthwindMiscellaneousQueryMySqlTest).FullName!,
            "StaticMethod"),
        (await Assert.ThrowsAsync<InvalidOperationException>(() =>
            base.Client_code_using_instance_in_static_method(async))).Message);

    public override async Task Client_code_using_instance_method_throws(
        bool async
    ) => Assert.Equal(
        CoreStrings.ClientProjectionCapturingConstantInMethodInstance(
            typeof(NorthwindMiscellaneousQueryMySqlTest).FullName!,
            "InstanceMethod"),
        (await Assert.ThrowsAsync<InvalidOperationException>(() =>
            base.Client_code_using_instance_method_throws(async))).Message);

    public override async Task Entity_equality_through_subquery_composite_key(
        bool async
    ) => Assert.Equal(
        CoreStrings.EntityEqualityOnCompositeKeyEntitySubqueryNotSupported("==", nameof(OrderDetail)),
        (await Assert.ThrowsAsync<InvalidOperationException>(() =>
            base.Entity_equality_through_subquery_composite_key(async))).Message);

    public override async Task Max_on_empty_sequence_throws(
        bool async
    ) => await Assert.ThrowsAsync<InvalidOperationException>(() => base.Max_on_empty_sequence_throws(async));

    public override Task
        Select_DTO_constructor_distinct_with_collection_projection_translated_to_server_with_binding_after_client_eval(
            bool async
        ) => base
        .Select_DTO_constructor_distinct_with_collection_projection_translated_to_server_with_binding_after_client_eval(
            async);
}

/// <summary>
/// Executes the official relational navigation-query contract on the provider.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public class NorthwindNavigationsQueryMySqlTest : NorthwindNavigationsQueryRelationalTestBase<
    NorthwindQueryMySqlFixture<NoopModelCustomizer>>
{
    public NorthwindNavigationsQueryMySqlTest(
        NorthwindQueryMySqlFixture<NoopModelCustomizer> fixture
    ) : base(fixture) { }

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Navigation_in_subquery_referencing_outer_query_with_client_side_result_operator_and_count(
        bool async
    ) => base.Navigation_in_subquery_referencing_outer_query_with_client_side_result_operator_and_count(async);
}

/// <summary>
/// Executes the official relational projection contract on the provider.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public class
    NorthwindSelectQueryMySqlTest : NorthwindSelectQueryRelationalTestBase<
    NorthwindQueryMySqlFixture<NoopModelCustomizer>>
{
    public NorthwindSelectQueryMySqlTest(
        NorthwindQueryMySqlFixture<NoopModelCustomizer> fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    // These projections need per-outer-row composition after pagination,
    // set operations, or nested shaping and therefore require LATERAL.

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Collection_projection_selecting_outer_element_followed_by_take(
        bool async
    ) => base.Collection_projection_selecting_outer_element_followed_by_take(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task
        Project_single_element_from_collection_with_OrderBy_Distinct_and_FirstOrDefault_followed_by_projecting_length(
            bool async
        ) => base
        .Project_single_element_from_collection_with_OrderBy_Distinct_and_FirstOrDefault_followed_by_projecting_length(
            async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Reverse_in_SelectMany_with_Take(
        bool async
    ) => base.Reverse_in_SelectMany_with_Take(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Reverse_in_projection_subquery_single_result(
        bool async
    ) => base.Reverse_in_projection_subquery_single_result(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task SelectMany_correlated_with_outer_2(
        bool async
    ) => base.SelectMany_correlated_with_outer_2(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task SelectMany_correlated_with_outer_4(
        bool async
    ) => base.SelectMany_correlated_with_outer_4(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task SelectMany_correlated_with_outer_6(
        bool async
    ) => base.SelectMany_correlated_with_outer_6(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task SelectMany_correlated_with_outer_7(
        bool async
    ) => base.SelectMany_correlated_with_outer_7(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Select_nested_collection_deep(
        bool async
    ) => base.Select_nested_collection_deep(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Select_nested_collection_deep_distinct_no_identifiers(
        bool async
    ) => base.Select_nested_collection_deep_distinct_no_identifiers(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Set_operation_in_pending_collection(
        bool async
    ) => base.Set_operation_in_pending_collection(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Take_on_correlated_collection_in_first(
        bool async
    ) => base.Take_on_correlated_collection_in_first(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Take_on_top_level_and_on_collection_projection_with_outer_apply(
        bool async
    ) => base.Take_on_top_level_and_on_collection_projection_with_outer_apply(async);

    public override async Task
        Correlated_collection_after_distinct_with_complex_projection_not_containing_original_identifier(
            bool async
        ) => Assert.Equal(
        RelationalStrings.InsufficientInformationToIdentifyElementOfCollectionJoin,
        (await Assert.ThrowsAsync<InvalidOperationException>(() =>
            base.Correlated_collection_after_distinct_with_complex_projection_not_containing_original_identifier(
                async))).Message);

    public override Task Member_binding_after_ctor_arguments_fails_with_client_eval(
        bool async
    ) => AssertTranslationFailed(() => base.Member_binding_after_ctor_arguments_fails_with_client_eval(async));

    /// <summary>
    /// Preserves EF Core's specific unmapped-property translation contract instead
    /// of reducing every translation failure to the generic query message.
    /// </summary>
    public override async Task
        SelectMany_with_collection_being_correlated_subquery_which_references_non_mapped_properties_from_inner_and_outer_entity(
            bool async
        ) => await AssertUnableToTranslateEFProperty(() =>
        base
            .SelectMany_with_collection_being_correlated_subquery_which_references_non_mapped_properties_from_inner_and_outer_entity(
                async));
}

/// <summary>
/// Executes the official relational set-operation contract on the provider.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public class NorthwindSetOperationsQueryMySqlTest : NorthwindSetOperationsQueryRelationalTestBase<
    NorthwindQueryMySqlFixture<NoopModelCustomizer>>
{
    public NorthwindSetOperationsQueryMySqlTest(
        NorthwindQueryMySqlFixture<NoopModelCustomizer> fixture
    ) : base(fixture) { }

    public override async Task Client_eval_Union_FirstOrDefault(
        bool async
    ) => Assert.Equal(
        RelationalStrings.SetOperationsNotAllowedAfterClientEvaluation,
        (await Assert.ThrowsAsync<InvalidOperationException>(() => base.Client_eval_Union_FirstOrDefault(async)))
        .Message);
}

/// <summary>
/// Executes the official Northwind SQL-query contract on the provider.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public class NorthwindSqlQueryMySqlTest : NorthwindSqlQueryTestBase<NorthwindQueryMySqlFixture<NoopModelCustomizer>>
{
    public NorthwindSqlQueryMySqlTest(
        NorthwindQueryMySqlFixture<NoopModelCustomizer> fixture
    ) : base(fixture) { }

    protected override DbParameter CreateDbParameter(
        string name,
        object value
    ) => new MySqlParameter(name, value);
}
