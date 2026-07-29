using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.TestModels.Northwind;
using Xunit.Abstractions;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query;

/// <summary>
/// Executes the official EF.Property include contract on the provider.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public class
    NorthwindEfPropertyIncludeQueryMySqlTest : NorthwindEFPropertyIncludeQueryTestBase<
    NorthwindQueryMySqlFixture<NoopModelCustomizer>>
{
    public NorthwindEfPropertyIncludeQueryMySqlTest(
        NorthwindQueryMySqlFixture<NoopModelCustomizer> fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    /// <summary>
    /// Adds the entity key as the final ordering component before applying
    /// <c>Take</c>, so equal conditional sort keys have deterministic semantics.
    /// </summary>
    public override Task Include_collection_with_multiple_conditional_order_by(
        bool async
    ) => AssertQuery(
        async,
        ss => ss
            .Set<Order>()
            .Include(order => order.OrderDetails)
            .OrderBy(order => order.OrderID > 0)
            .ThenBy(order => order.Customer != null ? order.Customer.City : string.Empty)
            .ThenBy(order => order.OrderID)
            .Take(5),
        elementAsserter: (
            expected,
            actual
        ) => AssertInclude(
            expected,
            actual,
            new ExpectedInclude<Order>(order => order.OrderDetails)));

    /// <summary>
    /// Adds the order key as a deterministic tie-breaker before applying
    /// <c>Take</c> to multiple orders belonging to the same customer.
    /// </summary>
    public override Task Repro9735(
        bool async
    ) => AssertQuery(
        async,
        ss => ss
            .Set<Order>()
            .Include(order => order.OrderDetails)
            .OrderBy(order => order.Customer.CustomerID != null)
            .ThenBy(order => order.Customer != null ? order.Customer.CustomerID : string.Empty)
            .ThenBy(order => order.OrderID)
            .Take(2),
        elementAsserter: (
            expected,
            actual
        ) => AssertInclude(
            expected,
            actual,
            new ExpectedInclude<Order>(order => order.OrderDetails)));

    // These include shapers require a correlated derived table. MariaDB cannot
    // express that boundary because its JOIN grammar has no LATERAL production.

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Filtered_include_with_multiple_ordering(
        bool async
    ) => base.Filtered_include_with_multiple_ordering(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Include_collection_with_cross_apply_with_filter(
        bool async
    ) => base.Include_collection_with_cross_apply_with_filter(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Include_collection_with_outer_apply_with_filter(
        bool async
    ) => base.Include_collection_with_outer_apply_with_filter(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Include_collection_with_outer_apply_with_filter_non_equality(
        bool async
    ) => base.Include_collection_with_outer_apply_with_filter_non_equality(async);

    /// <summary>
    /// Verifies duplicate collection includes with an explicit total order before
    /// the outer result operator is applied.
    /// </summary>
    public override Task Include_duplicate_collection_result_operator(
        bool async
    ) => AssertQuery(
        async,
        ss => (
            from c1 in ss
                .Set<Customer>()
                .Include(customer => customer.Orders)
                .OrderBy(customer => customer.CustomerID)
                .Take(2)
            from c2 in ss
                .Set<Customer>()
                .Include(customer => customer.Orders)
                .OrderBy(customer => customer.CustomerID)
                .Skip(2)
                .Take(2)
            orderby c1.CustomerID, c2.CustomerID
            select new { c1, c2, }
        ).Take(1),
        elementSorter: element => (element.c1.CustomerID, element.c2.CustomerID),
        elementAsserter: (
            expected,
            actual
        ) =>
        {
            AssertInclude(
                expected.c1,
                actual.c1,
                new ExpectedInclude<Customer>(customer => customer.Orders));
            AssertInclude(
                expected.c2,
                actual.c2,
                new ExpectedInclude<Customer>(customer => customer.Orders));
        });

    /// <summary>
    /// Verifies one duplicate collection include with an explicit total order
    /// before the outer result operator is applied.
    /// </summary>
    public override Task Include_duplicate_collection_result_operator2(
        bool async
    ) => AssertQuery(
        async,
        ss => (
            from c1 in ss
                .Set<Customer>()
                .Include(customer => customer.Orders)
                .OrderBy(customer => customer.CustomerID)
                .Take(2)
            from c2 in ss
                .Set<Customer>()
                .OrderBy(customer => customer.CustomerID)
                .Skip(2)
                .Take(2)
            orderby c1.CustomerID, c2.CustomerID
            select new { c1, c2, }
        ).Take(1),
        elementSorter: element => (element.c1.CustomerID, element.c2.CustomerID),
        elementAsserter: (
            expected,
            actual
        ) =>
        {
            AssertInclude(
                expected.c1,
                actual.c1,
                new ExpectedInclude<Customer>(customer => customer.Orders));
            AssertEqual(expected.c2, actual.c2);
        });

    public override async Task Include_collection_with_last_no_orderby(
        bool async
    ) => Assert.Equal(
        RelationalStrings.LastUsedWithoutOrderBy(nameof(Enumerable.Last)),
        (await Assert.ThrowsAsync<InvalidOperationException>(
            () => base.Include_collection_with_last_no_orderby(async))).Message);
}

/// <summary>
/// Executes the official relational include contract on the provider.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public class
    NorthwindIncludeQueryMySqlTest : NorthwindIncludeQueryRelationalTestBase<
    NorthwindQueryMySqlFixture<NoopModelCustomizer>>
{
    public NorthwindIncludeQueryMySqlTest(
        NorthwindQueryMySqlFixture<NoopModelCustomizer> fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    /// <summary>
    /// Adds the entity key as the final ordering component before applying
    /// <c>Take</c>, so equal conditional sort keys have deterministic semantics.
    /// </summary>
    public override Task Include_collection_with_multiple_conditional_order_by(
        bool async
    ) => AssertQuery(
        async,
        ss => ss
            .Set<Order>()
            .Include(order => order.OrderDetails)
            .OrderBy(order => order.OrderID > 0)
            .ThenBy(order => order.Customer != null ? order.Customer.City : string.Empty)
            .ThenBy(order => order.OrderID)
            .Take(5),
        elementAsserter: (
            expected,
            actual
        ) => AssertInclude(
            expected,
            actual,
            new ExpectedInclude<Order>(order => order.OrderDetails)));

    /// <summary>
    /// Adds the order key as a deterministic tie-breaker before applying
    /// <c>Take</c> to multiple orders belonging to the same customer.
    /// </summary>
    public override Task Repro9735(
        bool async
    ) => AssertQuery(
        async,
        ss => ss
            .Set<Order>()
            .Include(order => order.OrderDetails)
            .OrderBy(order => order.Customer.CustomerID != null)
            .ThenBy(order => order.Customer != null ? order.Customer.CustomerID : string.Empty)
            .ThenBy(order => order.OrderID)
            .Take(2),
        elementAsserter: (
            expected,
            actual
        ) => AssertInclude(
            expected,
            actual,
            new ExpectedInclude<Order>(order => order.OrderDetails)));

    // These include shapers require a correlated derived table. MariaDB cannot
    // express that boundary because its JOIN grammar has no LATERAL production.

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Filtered_include_with_multiple_ordering(
        bool async
    ) => base.Filtered_include_with_multiple_ordering(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Include_collection_with_cross_apply_with_filter(
        bool async
    ) => base.Include_collection_with_cross_apply_with_filter(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Include_collection_with_outer_apply_with_filter(
        bool async
    ) => base.Include_collection_with_outer_apply_with_filter(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Include_collection_with_outer_apply_with_filter_non_equality(
        bool async
    ) => base.Include_collection_with_outer_apply_with_filter_non_equality(async);

    /// <summary>
    /// Verifies duplicate collection includes with an explicit total order before
    /// the outer result operator is applied.
    /// </summary>
    public override Task Include_duplicate_collection_result_operator(
        bool async
    ) => AssertQuery(
        async,
        ss => (
            from c1 in ss
                .Set<Customer>()
                .Include(customer => customer.Orders)
                .OrderBy(customer => customer.CustomerID)
                .Take(2)
            from c2 in ss
                .Set<Customer>()
                .Include(customer => customer.Orders)
                .OrderBy(customer => customer.CustomerID)
                .Skip(2)
                .Take(2)
            orderby c1.CustomerID, c2.CustomerID
            select new { c1, c2, }
        ).Take(1),
        elementSorter: element => (element.c1.CustomerID, element.c2.CustomerID),
        elementAsserter: (
            expected,
            actual
        ) =>
        {
            AssertInclude(
                expected.c1,
                actual.c1,
                new ExpectedInclude<Customer>(customer => customer.Orders));
            AssertInclude(
                expected.c2,
                actual.c2,
                new ExpectedInclude<Customer>(customer => customer.Orders));
        });

    /// <summary>
    /// Verifies one duplicate collection include with an explicit total order
    /// before the outer result operator is applied.
    /// </summary>
    public override Task Include_duplicate_collection_result_operator2(
        bool async
    ) => AssertQuery(
        async,
        ss => (
            from c1 in ss
                .Set<Customer>()
                .Include(customer => customer.Orders)
                .OrderBy(customer => customer.CustomerID)
                .Take(2)
            from c2 in ss
                .Set<Customer>()
                .OrderBy(customer => customer.CustomerID)
                .Skip(2)
                .Take(2)
            orderby c1.CustomerID, c2.CustomerID
            select new { c1, c2, }
        ).Take(1),
        elementSorter: element => (element.c1.CustomerID, element.c2.CustomerID),
        elementAsserter: (
            expected,
            actual
        ) =>
        {
            AssertInclude(
                expected.c1,
                actual.c1,
                new ExpectedInclude<Customer>(customer => customer.Orders));
            AssertEqual(expected.c2, actual.c2);
        });
}

/// <summary>
/// Executes the official no-tracking include contract on the provider.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public class
    NorthwindIncludeNoTrackingQueryMySqlTest : NorthwindIncludeNoTrackingQueryTestBase<
    NorthwindQueryMySqlFixture<NoopModelCustomizer>>
{
    public NorthwindIncludeNoTrackingQueryMySqlTest(
        NorthwindQueryMySqlFixture<NoopModelCustomizer> fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    /// <summary>
    /// Adds the entity key as the final ordering component before applying
    /// <c>Take</c>, so equal conditional sort keys have deterministic semantics.
    /// </summary>
    public override Task Include_collection_with_multiple_conditional_order_by(
        bool async
    ) => AssertQuery(
        async,
        ss => ss
            .Set<Order>()
            .Include(order => order.OrderDetails)
            .OrderBy(order => order.OrderID > 0)
            .ThenBy(order => order.Customer != null ? order.Customer.City : string.Empty)
            .ThenBy(order => order.OrderID)
            .Take(5),
        elementAsserter: (
            expected,
            actual
        ) => AssertInclude(
            expected,
            actual,
            new ExpectedInclude<Order>(order => order.OrderDetails)));

    /// <summary>
    /// Adds the order key as a deterministic tie-breaker before applying
    /// <c>Take</c> to multiple orders belonging to the same customer.
    /// </summary>
    public override Task Repro9735(
        bool async
    ) => AssertQuery(
        async,
        ss => ss
            .Set<Order>()
            .Include(order => order.OrderDetails)
            .OrderBy(order => order.Customer.CustomerID != null)
            .ThenBy(order => order.Customer != null ? order.Customer.CustomerID : string.Empty)
            .ThenBy(order => order.OrderID)
            .Take(2),
        elementAsserter: (
            expected,
            actual
        ) => AssertInclude(
            expected,
            actual,
            new ExpectedInclude<Order>(order => order.OrderDetails)));

    // These no-tracking include shapers require a correlated derived table.

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Filtered_include_with_multiple_ordering(
        bool async
    ) => base.Filtered_include_with_multiple_ordering(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Include_collection_with_cross_apply_with_filter(
        bool async
    ) => base.Include_collection_with_cross_apply_with_filter(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Include_collection_with_outer_apply_with_filter(
        bool async
    ) => base.Include_collection_with_outer_apply_with_filter(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Include_collection_with_outer_apply_with_filter_non_equality(
        bool async
    ) => base.Include_collection_with_outer_apply_with_filter_non_equality(async);

    /// <summary>
    /// Verifies duplicate no-tracking collection includes with an explicit total
    /// order before the outer result operator is applied.
    /// </summary>
    public override Task Include_duplicate_collection_result_operator(
        bool async
    ) => AssertQuery(
        async,
        ss => (
            from c1 in ss
                .Set<Customer>()
                .Include(customer => customer.Orders)
                .OrderBy(customer => customer.CustomerID)
                .Take(2)
            from c2 in ss
                .Set<Customer>()
                .Include(customer => customer.Orders)
                .OrderBy(customer => customer.CustomerID)
                .Skip(2)
                .Take(2)
            orderby c1.CustomerID, c2.CustomerID
            select new { c1, c2, }
        ).Take(1),
        elementSorter: element => (element.c1.CustomerID, element.c2.CustomerID),
        elementAsserter: (
            expected,
            actual
        ) =>
        {
            AssertInclude(
                expected.c1,
                actual.c1,
                new ExpectedInclude<Customer>(customer => customer.Orders));
            AssertInclude(
                expected.c2,
                actual.c2,
                new ExpectedInclude<Customer>(customer => customer.Orders));
        });

    /// <summary>
    /// Verifies one duplicate no-tracking collection include with an explicit
    /// total order before the outer result operator is applied.
    /// </summary>
    public override Task Include_duplicate_collection_result_operator2(
        bool async
    ) => AssertQuery(
        async,
        ss => (
            from c1 in ss
                .Set<Customer>()
                .Include(customer => customer.Orders)
                .OrderBy(customer => customer.CustomerID)
                .Take(2)
            from c2 in ss
                .Set<Customer>()
                .OrderBy(customer => customer.CustomerID)
                .Skip(2)
                .Take(2)
            orderby c1.CustomerID, c2.CustomerID
            select new { c1, c2, }
        ).Take(1),
        elementSorter: element => (element.c1.CustomerID, element.c2.CustomerID),
        elementAsserter: (
            expected,
            actual
        ) =>
        {
            AssertInclude(
                expected.c1,
                actual.c1,
                new ExpectedInclude<Customer>(customer => customer.Orders));
            AssertEqual(expected.c2, actual.c2);
        });

    public override async Task Include_collection_with_last_no_orderby(
        bool async
    ) => Assert.Equal(
        RelationalStrings.LastUsedWithoutOrderBy(nameof(Enumerable.Last)),
        (await Assert.ThrowsAsync<InvalidOperationException>(
            () => base.Include_collection_with_last_no_orderby(async))).Message);
}

/// <summary>
/// Executes the official split-query include contract on the provider.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public class
    NorthwindSplitIncludeQueryMySqlTest : NorthwindSplitIncludeQueryTestBase<
    NorthwindQueryMySqlFixture<NoopModelCustomizer>>
{
    public NorthwindSplitIncludeQueryMySqlTest(
        NorthwindQueryMySqlFixture<NoopModelCustomizer> fixture
    ) : base(fixture) { }

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Filtered_include_with_multiple_ordering(
        bool async
    ) => base.Filtered_include_with_multiple_ordering(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Include_collection_with_cross_apply_with_filter(
        bool async
    ) => base.Include_collection_with_cross_apply_with_filter(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Include_collection_with_outer_apply_with_filter(
        bool async
    ) => base.Include_collection_with_outer_apply_with_filter(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Include_collection_with_outer_apply_with_filter_non_equality(
        bool async
    ) => base.Include_collection_with_outer_apply_with_filter_non_equality(async);
}

/// <summary>
/// Executes the official no-tracking split-query include contract on the provider.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public class NorthwindSplitIncludeNoTrackingQueryMySqlTest : NorthwindSplitIncludeNoTrackingQueryTestBase<
    NorthwindQueryMySqlFixture<NoopModelCustomizer>>
{
    public NorthwindSplitIncludeNoTrackingQueryMySqlTest(
        NorthwindQueryMySqlFixture<NoopModelCustomizer> fixture
    ) : base(fixture) { }

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Filtered_include_with_multiple_ordering(
        bool async
    ) => base.Filtered_include_with_multiple_ordering(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Include_collection_with_cross_apply_with_filter(
        bool async
    ) => base.Include_collection_with_cross_apply_with_filter(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Include_collection_with_outer_apply_with_filter(
        bool async
    ) => base.Include_collection_with_outer_apply_with_filter(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Include_collection_with_outer_apply_with_filter_non_equality(
        bool async
    ) => base.Include_collection_with_outer_apply_with_filter_non_equality(async);
}

/// <summary>
/// Executes the official string-based include contract on the provider.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public class
    NorthwindStringIncludeQueryMySqlTest : NorthwindStringIncludeQueryTestBase<
    NorthwindQueryMySqlFixture<NoopModelCustomizer>>
{
    public NorthwindStringIncludeQueryMySqlTest(
        NorthwindQueryMySqlFixture<NoopModelCustomizer> fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    /// <summary>
    /// Adds the entity key as the final ordering component before applying
    /// <c>Take</c>, so equal conditional sort keys have deterministic semantics.
    /// </summary>
    public override Task Include_collection_with_multiple_conditional_order_by(
        bool async
    ) => AssertQuery(
        async,
        ss => ss
            .Set<Order>()
            .Include(order => order.OrderDetails)
            .OrderBy(order => order.OrderID > 0)
            .ThenBy(order => order.Customer != null ? order.Customer.City : string.Empty)
            .ThenBy(order => order.OrderID)
            .Take(5),
        elementAsserter: (
            expected,
            actual
        ) => AssertInclude(
            expected,
            actual,
            new ExpectedInclude<Order>(order => order.OrderDetails)));

    /// <summary>
    /// Adds the order key as a deterministic tie-breaker before applying
    /// <c>Take</c> to multiple orders belonging to the same customer.
    /// </summary>
    public override Task Repro9735(
        bool async
    ) => AssertQuery(
        async,
        ss => ss
            .Set<Order>()
            .Include(order => order.OrderDetails)
            .OrderBy(order => order.Customer.CustomerID != null)
            .ThenBy(order => order.Customer != null ? order.Customer.CustomerID : string.Empty)
            .ThenBy(order => order.OrderID)
            .Take(2),
        elementAsserter: (
            expected,
            actual
        ) => AssertInclude(
            expected,
            actual,
            new ExpectedInclude<Order>(order => order.OrderDetails)));

    // String-based include rewriting reaches the same MariaDB LATERAL boundary.

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Include_collection_with_cross_apply_with_filter(
        bool async
    ) => base.Include_collection_with_cross_apply_with_filter(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Include_collection_with_outer_apply_with_filter(
        bool async
    ) => base.Include_collection_with_outer_apply_with_filter(async);

    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Include_collection_with_outer_apply_with_filter_non_equality(
        bool async
    ) => base.Include_collection_with_outer_apply_with_filter_non_equality(async);

    /// <summary>
    /// Verifies duplicate string-based collection includes with an explicit total
    /// order before the outer result operator is applied.
    /// </summary>
    public override Task Include_duplicate_collection_result_operator(
        bool async
    ) => AssertQuery(
        async,
        ss => (
            from c1 in ss
                .Set<Customer>()
                .Include(customer => customer.Orders)
                .OrderBy(customer => customer.CustomerID)
                .Take(2)
            from c2 in ss
                .Set<Customer>()
                .Include(customer => customer.Orders)
                .OrderBy(customer => customer.CustomerID)
                .Skip(2)
                .Take(2)
            orderby c1.CustomerID, c2.CustomerID
            select new { c1, c2, }
        ).Take(1),
        elementSorter: element => (element.c1.CustomerID, element.c2.CustomerID),
        elementAsserter: (
            expected,
            actual
        ) =>
        {
            AssertInclude(
                expected.c1,
                actual.c1,
                new ExpectedInclude<Customer>(customer => customer.Orders));
            AssertInclude(
                expected.c2,
                actual.c2,
                new ExpectedInclude<Customer>(customer => customer.Orders));
        });

    /// <summary>
    /// Verifies one duplicate string-based collection include with an explicit
    /// total order before the outer result operator is applied.
    /// </summary>
    public override Task Include_duplicate_collection_result_operator2(
        bool async
    ) => AssertQuery(
        async,
        ss => (
            from c1 in ss
                .Set<Customer>()
                .Include(customer => customer.Orders)
                .OrderBy(customer => customer.CustomerID)
                .Take(2)
            from c2 in ss
                .Set<Customer>()
                .OrderBy(customer => customer.CustomerID)
                .Skip(2)
                .Take(2)
            orderby c1.CustomerID, c2.CustomerID
            select new { c1, c2, }
        ).Take(1),
        elementSorter: element => (element.c1.CustomerID, element.c2.CustomerID),
        elementAsserter: (
            expected,
            actual
        ) =>
        {
            AssertInclude(
                expected.c1,
                actual.c1,
                new ExpectedInclude<Customer>(customer => customer.Orders));
            AssertEqual(expected.c2, actual.c2);
        });

    public override async Task Include_collection_with_last_no_orderby(
        bool async
    ) => Assert.Equal(
        RelationalStrings.LastUsedWithoutOrderBy(nameof(Enumerable.Last)),
        (await Assert.ThrowsAsync<InvalidOperationException>(
            () => base.Include_collection_with_last_no_orderby(async))).Message);
}
