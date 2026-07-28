using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Microsoft.EntityFrameworkCore.BulkUpdates;
using Microsoft.EntityFrameworkCore.TestModels.Northwind;
using Xunit.Abstractions;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.BulkUpdates;

/// <summary>
/// Executes the official relational Northwind bulk-update contract through the
/// provider's real update and delete translation pipeline.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class NorthwindBulkUpdatesMySqlTest
    : NorthwindBulkUpdatesRelationalTestBase<
        NorthwindBulkUpdatesMySqlFixture<NoopModelCustomizer>>
{
    public NorthwindBulkUpdatesMySqlTest(
        NorthwindBulkUpdatesMySqlFixture<NoopModelCustomizer> fixture,
        ITestOutputHelper testOutputHelper
    ) : base(
        fixture,
        testOutputHelper)
    {
    }

    /// <summary>
    /// Verifies that lifting a simple correlated CROSS APPLY to an inner join
    /// preserves every duplicate produced by the right-hand rowset.
    /// </summary>
    [Fact]
    public async Task Apply_rewrite_preserves_cross_apply_multiplicity()
    {
        var createContext = Fixture.GetContextCreator();
        await using var context =
            (NorthwindBulkUpdatesMySqlContext)createContext();

        var customerCount = await context.Customers
            .CountAsync(
                customer => EF.Functions.Like(
                    customer.CustomerID,
                    "F%"));
        var orderCount = await context.Orders
            .CountAsync(order => order.OrderID > 5);
        var rows = await (
                from customer in context.Customers
                where EF.Functions.Like(customer.CustomerID, "F%")
                from order in context.Orders.Where(
                    order => order.OrderID > customer.CustomerID.Length)
                select new
                {
                    customer.CustomerID,
                    order.OrderID,
                })
            .ToListAsync();

        Assert.Equal(customerCount * orderCount, rows.Count);
    }

    /// <summary>
    /// Verifies that lifting a simple correlated OUTER APPLY to a left join
    /// retains one null-extended row when its right-hand rowset is empty.
    /// </summary>
    [Fact]
    public async Task Apply_rewrite_preserves_outer_apply_null_extension()
    {
        var createContext = Fixture.GetContextCreator();
        await using var context =
            (NorthwindBulkUpdatesMySqlContext)createContext();

        var expectedCount = await context.Customers
            .CountAsync(
                customer => EF.Functions.Like(
                    customer.CustomerID,
                    "F%"));
        var rows = await (
                from customer in context.Customers
                where EF.Functions.Like(customer.CustomerID, "F%")
                from order in context.Orders
                    .Where(
                        order =>
                            order.OrderID < customer.CustomerID.Length)
                    .DefaultIfEmpty()
                select new
                {
                    customer.CustomerID,
                    OrderId = (int?)order!.OrderID,
                })
            .ToListAsync();

        Assert.Equal(expectedCount, rows.Count);
        Assert.All(rows, row => Assert.Null(row.OrderId));
    }

    /// <summary>
    /// Activates the upstream-skipped grouped delete projection.
    /// </summary>
    [SpecFrameworkLimitationTheory("EFCORE-28525-BULK-ENTITY-PROJECTION")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Delete_GroupBy_Where_Select(
        bool async
    ) => base.Delete_GroupBy_Where_Select(async);

    /// <summary>
    /// Activates the upstream-skipped grouped scalar delete predicate.
    /// </summary>
    [SpecFrameworkLimitationTheory("EFCORE-26753-GROUPING-FIRST-PROPERTY")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Delete_GroupBy_Where_Select_2(
        bool async
    ) => base.Delete_GroupBy_Where_Select_2(async);

    /// <summary>
    /// Activates the upstream-skipped grouped scalar update predicate.
    /// </summary>
    [SpecFrameworkLimitationTheory("EFCORE-26753-GROUPING-FIRST-PROPERTY")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Update_Where_GroupBy_First_set_constant_2(
        bool async
    ) => base.Update_Where_GroupBy_First_set_constant_2(async);
}

/// <summary>
/// MySQL fixture for the official Northwind bulk-update model.
/// </summary>
public sealed class NorthwindBulkUpdatesMySqlFixture<TModelCustomizer>
    : NorthwindBulkUpdatesRelationalFixture<TModelCustomizer>
    where TModelCustomizer : ITestModelCustomizer, new()
{
    protected override ITestStoreFactory TestStoreFactory =>
        MySqlNorthwindBulkUpdatesTestStoreFactory.Instance;

    protected override Type ContextType => typeof(NorthwindBulkUpdatesMySqlContext);
}

/// <summary>
/// Applies MySQL-compatible store types to the official Northwind model.
/// </summary>
public sealed class NorthwindBulkUpdatesMySqlContext : NorthwindRelationalContext
{
    public NorthwindBulkUpdatesMySqlContext(
        DbContextOptions options
    ) : base(options)
    {
    }

    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Employee>(builder =>
        {
            builder.Property(entity => entity.EmployeeID).HasColumnType("int");
            builder.Property(entity => entity.ReportsTo).HasColumnType("int");
        });

        modelBuilder.Entity<Customer>()
            .Property(entity => entity.CustomerID)
            .IsFixedLength();

        modelBuilder.Entity<Order>(builder =>
        {
            builder.Property(entity => entity.CustomerID).IsFixedLength();
            builder.Property(entity => entity.EmployeeID).HasColumnType("int");
            builder.Property(entity => entity.OrderDate).HasColumnType("datetime");
        });

        modelBuilder.Entity<Product>()
            .Property(entity => entity.UnitsInStock)
            .HasColumnType("smallint");

        modelBuilder.Entity<OrderDetail>(builder =>
        {
            builder.Property(entity => entity.Quantity).HasColumnType("smallint");
            builder.Property(entity => entity.Discount).HasColumnType("float");
        });
    }
}

/// <summary>
/// Executes non-shared-model bulk updates, including table splitting and
/// entity-splitting shapes.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class NonSharedModelBulkUpdatesMySqlTest
    : NonSharedModelBulkUpdatesRelationalTestBase
{
    public NonSharedModelBulkUpdatesMySqlTest(
        NonSharedFixture fixture
    ) : base(fixture)
    {
    }

    protected override ITestStoreFactory TestStoreFactory =>
        MySqlTestStoreFactory.Instance;
}

/// <summary>
/// Executes the official TPC filtered-inheritance bulk-update contract.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class TpcFiltersInheritanceBulkUpdatesMySqlTest
    : TPCFiltersInheritanceBulkUpdatesTestBase<
        TpcFiltersInheritanceBulkUpdatesMySqlFixture>
{
    public TpcFiltersInheritanceBulkUpdatesMySqlTest(
        TpcFiltersInheritanceBulkUpdatesMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(
        fixture,
        testOutputHelper)
    {
    }

    /// <summary>
    /// Activates the upstream-skipped grouped delete projection.
    /// </summary>
    [SpecFrameworkLimitationTheory("EFCORE-28525-BULK-ENTITY-PROJECTION")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Delete_GroupBy_Where_Select_First(
        bool async
    ) => base.Delete_GroupBy_Where_Select_First(async);

    /// <summary>
    /// Activates the upstream-skipped grouped scalar delete predicate.
    /// </summary>
    [SpecFrameworkLimitationTheory("EFCORE-26753-GROUPING-FIRST-PROPERTY")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Delete_GroupBy_Where_Select_First_2(
        bool async
    ) => base.Delete_GroupBy_Where_Select_First_2(async);

    /// <summary>
    /// Activates the upstream-skipped hierarchy update subquery.
    /// </summary>
    [SpecFrameworkLimitationTheory("EFCORE-TPC-NONLEAF-BULK-UPDATE")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Update_where_hierarchy_subquery(
        bool async
    ) => base.Update_where_hierarchy_subquery(async);

    protected override void ClearLog() =>
        Fixture.TestSqlLoggerFactory.Clear();
}

/// <summary>
/// MySQL fixture for filtered TPC bulk updates.
/// </summary>
public sealed class TpcFiltersInheritanceBulkUpdatesMySqlFixture
    : TpcInheritanceBulkUpdatesMySqlFixture
{
    protected override string StoreName =>
        "TpcFiltersInheritanceBulkUpdatesMySql";

    public override bool EnableFilters => true;
}

/// <summary>
/// Executes the official TPC inheritance bulk-update contract.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class TpcInheritanceBulkUpdatesMySqlTest
    : TPCInheritanceBulkUpdatesTestBase<
        TpcInheritanceBulkUpdatesMySqlFixture>
{
    public TpcInheritanceBulkUpdatesMySqlTest(
        TpcInheritanceBulkUpdatesMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(
        fixture,
        testOutputHelper)
    {
    }

    /// <summary>
    /// Activates the upstream-skipped grouped delete projection.
    /// </summary>
    [SpecFrameworkLimitationTheory("EFCORE-28525-BULK-ENTITY-PROJECTION")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Delete_GroupBy_Where_Select_First(
        bool async
    ) => base.Delete_GroupBy_Where_Select_First(async);

    /// <summary>
    /// Activates the upstream-skipped grouped scalar delete predicate.
    /// </summary>
    [SpecFrameworkLimitationTheory("EFCORE-26753-GROUPING-FIRST-PROPERTY")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Delete_GroupBy_Where_Select_First_2(
        bool async
    ) => base.Delete_GroupBy_Where_Select_First_2(async);

    /// <summary>
    /// Activates the upstream-skipped hierarchy update subquery.
    /// </summary>
    [SpecFrameworkLimitationTheory("EFCORE-TPC-NONLEAF-BULK-UPDATE")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Update_where_hierarchy_subquery(
        bool async
    ) => base.Update_where_hierarchy_subquery(async);

    protected override void ClearLog() =>
        Fixture.TestSqlLoggerFactory.Clear();
}

/// <summary>
/// MySQL fixture for TPC bulk updates.
/// </summary>
public class TpcInheritanceBulkUpdatesMySqlFixture
    : TPCInheritanceBulkUpdatesFixture
{
    protected override ITestStoreFactory TestStoreFactory =>
        MySqlTestStoreFactory.Instance;

    public override bool UseGeneratedKeys => false;
}

/// <summary>
/// Executes the official TPH inheritance bulk-update contract.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class TphInheritanceBulkUpdatesMySqlTest
    : TPHInheritanceBulkUpdatesTestBase<
        TphInheritanceBulkUpdatesMySqlFixture>
{
    public TphInheritanceBulkUpdatesMySqlTest(
        TphInheritanceBulkUpdatesMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(
        fixture,
        testOutputHelper)
    {
    }

    /// <summary>
    /// Activates the upstream-skipped grouped delete projection.
    /// </summary>
    [SpecFrameworkLimitationTheory("EFCORE-28525-BULK-ENTITY-PROJECTION")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Delete_GroupBy_Where_Select_First(
        bool async
    ) => base.Delete_GroupBy_Where_Select_First(async);

    /// <summary>
    /// Activates the upstream-skipped grouped scalar delete predicate.
    /// </summary>
    [SpecFrameworkLimitationTheory("EFCORE-26753-GROUPING-FIRST-PROPERTY")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Delete_GroupBy_Where_Select_First_2(
        bool async
    ) => base.Delete_GroupBy_Where_Select_First_2(async);

    /// <summary>
    /// Executes the grouped self-referencing delete where immediate foreign-key
    /// enforcement permits the statement's complete target set.
    /// </summary>
    [SpecEngineLimitationTheory(
        "MYSQL-MARIADB-IMMEDIATE-SELF-FK-DELETE",
        "mysql84",
        "mariadb114")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Delete_GroupBy_Where_Select_First_3(
        bool async
    ) => base.Delete_GroupBy_Where_Select_First_3(async);

    /// <summary>
    /// Activates the upstream-skipped hierarchy update subquery.
    /// </summary>
    [DirectTheory]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Update_where_hierarchy_subquery(
        bool async
    ) => base.Update_where_hierarchy_subquery(async);
}

/// <summary>
/// MySQL fixture for TPH bulk updates.
/// </summary>
public sealed class TphInheritanceBulkUpdatesMySqlFixture
    : TPHInheritanceBulkUpdatesFixture
{
    protected override ITestStoreFactory TestStoreFactory =>
        MySqlTestStoreFactory.Instance;
}

/// <summary>
/// Executes the official TPT filtered-inheritance bulk-update contract.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class TptFiltersInheritanceBulkUpdatesMySqlTest
    : TPTFiltersInheritanceBulkUpdatesTestBase<
        TptFiltersInheritanceBulkUpdatesMySqlFixture>
{
    public TptFiltersInheritanceBulkUpdatesMySqlTest(
        TptFiltersInheritanceBulkUpdatesMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(
        fixture,
        testOutputHelper)
    {
    }

    /// <summary>
    /// Activates the upstream-skipped grouped delete projection.
    /// </summary>
    [SpecFrameworkLimitationTheory("EFCORE-28525-BULK-ENTITY-PROJECTION")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Delete_GroupBy_Where_Select_First(
        bool async
    ) => base.Delete_GroupBy_Where_Select_First(async);

    /// <summary>
    /// Activates the upstream-skipped grouped scalar delete predicate.
    /// </summary>
    [SpecFrameworkLimitationTheory("EFCORE-26753-GROUPING-FIRST-PROPERTY")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Delete_GroupBy_Where_Select_First_2(
        bool async
    ) => base.Delete_GroupBy_Where_Select_First_2(async);

    /// <summary>
    /// Activates the upstream-skipped hierarchy update subquery.
    /// </summary>
    [DirectTheory]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Update_where_hierarchy_subquery(
        bool async
    ) => base.Update_where_hierarchy_subquery(async);

    protected override void ClearLog() =>
        Fixture.TestSqlLoggerFactory.Clear();
}

/// <summary>
/// MySQL fixture for filtered TPT bulk updates.
/// </summary>
public sealed class TptFiltersInheritanceBulkUpdatesMySqlFixture
    : TptInheritanceBulkUpdatesMySqlFixture
{
    protected override string StoreName =>
        "TptFiltersInheritanceBulkUpdatesMySql";

    public override bool EnableFilters => true;
}

/// <summary>
/// Executes the official TPT inheritance bulk-update contract.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class TptInheritanceBulkUpdatesMySqlTest
    : TPTInheritanceBulkUpdatesTestBase<
        TptInheritanceBulkUpdatesMySqlFixture>
{
    public TptInheritanceBulkUpdatesMySqlTest(
        TptInheritanceBulkUpdatesMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(
        fixture,
        testOutputHelper)
    {
    }

    /// <summary>
    /// Activates both upstream-skipped hierarchy delete predicates.
    /// </summary>
    [DirectTheory]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Delete_where_using_hierarchy(
        bool async
    ) => base.Delete_where_using_hierarchy(async);

    /// <summary>
    /// Activates the upstream-skipped derived hierarchy delete predicate.
    /// </summary>
    [DirectTheory]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Delete_where_using_hierarchy_derived(
        bool async
    ) => base.Delete_where_using_hierarchy_derived(async);

    /// <summary>
    /// Activates the upstream-skipped grouped delete projection.
    /// </summary>
    [SpecFrameworkLimitationTheory("EFCORE-28525-BULK-ENTITY-PROJECTION")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Delete_GroupBy_Where_Select_First(
        bool async
    ) => base.Delete_GroupBy_Where_Select_First(async);

    /// <summary>
    /// Activates the upstream-skipped grouped scalar delete predicate.
    /// </summary>
    [SpecFrameworkLimitationTheory("EFCORE-26753-GROUPING-FIRST-PROPERTY")]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Delete_GroupBy_Where_Select_First_2(
        bool async
    ) => base.Delete_GroupBy_Where_Select_First_2(async);

    /// <summary>
    /// Activates the upstream-skipped hierarchy update subquery.
    /// </summary>
    [DirectTheory]
    [InlineData(false)]
    [InlineData(true)]
    public override Task Update_where_hierarchy_subquery(
        bool async
    ) => base.Update_where_hierarchy_subquery(async);

    protected override void ClearLog() =>
        Fixture.TestSqlLoggerFactory.Clear();
}

/// <summary>
/// MySQL fixture for TPT bulk updates.
/// </summary>
public class TptInheritanceBulkUpdatesMySqlFixture
    : TPTInheritanceBulkUpdatesFixture
{
    protected override ITestStoreFactory TestStoreFactory =>
        MySqlTestStoreFactory.Instance;
}
