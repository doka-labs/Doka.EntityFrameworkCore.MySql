using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Microsoft.EntityFrameworkCore.TestModels.Northwind;
using Xunit.Abstractions;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query;

public sealed class Ef6GroupByMySqlFixture : Ef6GroupByTestBase<Ef6GroupByMySqlFixture>.Ef6GroupByFixtureBase,
    ITestSqlLoggerFactory
{
    public TestSqlLoggerFactory TestSqlLoggerFactory => (TestSqlLoggerFactory)ListLoggerFactory;

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class Ef6GroupByMySqlTest : Ef6GroupByTestBase<Ef6GroupByMySqlFixture>
{
    public Ef6GroupByMySqlTest(
        Ef6GroupByMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }
}

public sealed class
    FunkyDataQueryMySqlFixture : FunkyDataQueryTestBase<FunkyDataQueryMySqlFixture>.FunkyDataQueryFixtureBase,
    ITestSqlLoggerFactory
{
    public TestSqlLoggerFactory TestSqlLoggerFactory => (TestSqlLoggerFactory)ListLoggerFactory;

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class FunkyDataQueryMySqlTest : FunkyDataQueryTestBase<FunkyDataQueryMySqlFixture>
{
    public FunkyDataQueryMySqlTest(
        FunkyDataQueryMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    protected override QueryAsserter CreateQueryAsserter(
        FunkyDataQueryMySqlFixture fixture
    ) => new RelationalQueryAsserter(fixture, RewriteExpectedQueryExpression, RewriteServerQueryExpression);
}

public sealed class
    IncludeOneToOneMySqlFixture : IncludeOneToOneTestBase<IncludeOneToOneMySqlFixture>.OneToOneQueryFixtureBase,
    ITestSqlLoggerFactory
{
    public TestSqlLoggerFactory TestSqlLoggerFactory => (TestSqlLoggerFactory)ListLoggerFactory;

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class IncludeOneToOneMySqlTest : IncludeOneToOneTestBase<IncludeOneToOneMySqlFixture>
{
    public IncludeOneToOneMySqlTest(
        IncludeOneToOneMySqlFixture fixture
    ) : base(fixture) { }
}

public sealed class NullKeysMySqlFixture : NullKeysTestBase<NullKeysMySqlFixture>.NullKeysFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class NullKeysMySqlTest : NullKeysTestBase<NullKeysMySqlFixture>
{
    public NullKeysMySqlTest(
        NullKeysMySqlFixture fixture
    ) : base(fixture) { }
}

public sealed class QueryFilterFuncletizationMySqlFixture : QueryFilterFuncletizationRelationalFixture
{
    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class
    QueryFilterFuncletizationMySqlTest : QueryFilterFuncletizationTestBase<QueryFilterFuncletizationMySqlFixture>
{
    public QueryFilterFuncletizationMySqlTest(
        QueryFilterFuncletizationMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class MappingQueryMySqlTest : MappingQueryTestBase<MappingQueryMySqlTest.MappingQueryMySqlFixture>
{
    public MappingQueryMySqlTest(
        MappingQueryMySqlFixture fixture
    ) : base(fixture) { }

    public sealed class MappingQueryMySqlFixture : MappingQueryFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlNorthwindTestStoreFactory.Instance;

        protected override bool RecreateStore => true;

        protected override string DatabaseSchema { get; } = null!;

        protected override void OnModelCreating(
            ModelBuilder modelBuilder,
            DbContext context
        )
        {
            base.OnModelCreating(modelBuilder, context);

            modelBuilder.Entity<MappedCustomer>(entity =>
            {
                entity
                    .Property(customer => customer.CompanyName2)
                    .Metadata.SetColumnName("CompanyName");
                entity.Metadata.SetTableName("Customers");
            });
        }

        protected override async Task SeedAsync(
            PoolableDbContext context
        )
        {
            var source = NorthwindData.Instance;

            context
                .Set<MappedCustomer>()
                .AddRange(
                    source.Customers.Select(customer => new MappedCustomer
                    {
                        CustomerID = customer.CustomerID,
                        CompanyName2 = customer.CompanyName,
                    }));
            context
                .Set<MappedEmployee>()
                .AddRange(
                    source.Employees.Select(employee => new MappedEmployee
                    {
                        EmployeeID = employee.EmployeeID,
                        City2 = employee.City,
                    }));
            context
                .Set<MappedOrder>()
                .AddRange(
                    source.Orders.Select(order => new MappedOrder
                    {
                        OrderID = order.OrderID,
                        ShipVia2 = order.ShipVia is null ? null : (ShipVia)order.ShipVia,
                    }));

            await context.SaveChangesAsync();
        }
    }
}
