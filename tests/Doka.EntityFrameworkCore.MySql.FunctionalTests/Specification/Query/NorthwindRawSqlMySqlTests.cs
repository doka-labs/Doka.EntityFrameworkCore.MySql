using Microsoft.EntityFrameworkCore.TestModels.ConcurrencyModel;
using Microsoft.EntityFrameworkCore.TestModels.Northwind;
using Xunit.Abstractions;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query;

/// <summary>
/// Runs raw entity SQL composition and parameterization through MySqlConnector parameters.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class FromSqlQueryMySqlTest : FromSqlQueryTestBase<NorthwindQueryMySqlFixture<NoopModelCustomizer>>
{
    public FromSqlQueryMySqlTest(
        NorthwindQueryMySqlFixture<NoopModelCustomizer> fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    protected override DbParameter CreateDbParameter(
        string name,
        object value
    ) => new MySqlParameter
    {
        ParameterName = name,
        Value = value,
    };

    public override Task Bad_data_error_handling_invalid_cast_key(
        bool async
    ) => AssertProductMaterializationError(
        async,
        """
        SELECT [ProductName] AS [ProductID],
               [ProductID] AS [ProductName],
               [SupplierID],
               [UnitPrice],
               [UnitsInStock],
               [Discontinued],
               [CategoryID],
               [QuantityPerUnit],
               [UnitsOnOrder],
               [ReorderLevel]
        FROM [Products]
        """,
        CoreStrings.ErrorMaterializingPropertyInvalidCast("Product", "ProductID", typeof(int), typeof(string)));

    public override Task Bad_data_error_handling_invalid_cast(
        bool async
    ) => AssertProductMaterializationError(
        async,
        """
        SELECT [ProductID],
               [SupplierID] AS [UnitPrice],
               [ProductName],
               [SupplierID],
               [UnitsInStock],
               [Discontinued],
               [CategoryID],
               [QuantityPerUnit],
               [UnitsOnOrder],
               [ReorderLevel]
        FROM [Products]
        """,
        CoreStrings.ErrorMaterializingPropertyInvalidCast("Product", "UnitPrice", typeof(decimal?), typeof(int)));

    public override Task Bad_data_error_handling_invalid_cast_no_tracking(
        bool async
    ) => AssertProductMaterializationError(
        async,
        """
        SELECT [ProductName] AS [ProductID],
               [ProductID] AS [ProductName],
               [SupplierID],
               [UnitPrice],
               [UnitsInStock],
               [Discontinued],
               [CategoryID],
               [QuantityPerUnit],
               [UnitsOnOrder],
               [ReorderLevel]
        FROM [Products]
        """,
        CoreStrings.ErrorMaterializingPropertyInvalidCast("Product", "ProductID", typeof(int), typeof(string)),
        noTracking: true);

    public override Task Bad_data_error_handling_null(
        bool async
    ) => AssertProductMaterializationError(
        async,
        """
        SELECT [ProductID],
               [ProductName],
               [SupplierID],
               [UnitPrice],
               [UnitsInStock],
               NULL AS [Discontinued],
               [CategoryID],
               [QuantityPerUnit],
               [UnitsOnOrder],
               [ReorderLevel]
        FROM [Products]
        """,
        RelationalStrings.ErrorMaterializingPropertyNullReference("Product", "Discontinued", typeof(bool)));

    public override Task Bad_data_error_handling_null_no_tracking(
        bool async
    ) => AssertProductMaterializationError(
        async,
        """
        SELECT [ProductID],
               [ProductName],
               [SupplierID],
               [UnitPrice],
               [UnitsInStock],
               NULL AS [Discontinued],
               [CategoryID],
               [QuantityPerUnit],
               [UnitsOnOrder],
               [ReorderLevel]
        FROM [Products]
        """,
        RelationalStrings.ErrorMaterializingPropertyNullReference("Product", "Discontinued", typeof(bool)),
        noTracking: true);

    private async Task AssertProductMaterializationError(
        bool async,
        string sql,
        string expectedMessage,
        bool noTracking = false
    )
    {
        using var context = CreateContext();
        var query = context
            .Set<Product>()
            .FromSqlRaw(NormalizeDelimitersInRawString(sql));

        if (noTracking)
        {
            query = query.AsNoTracking();
        }

        var exception = async
            ? await Assert.ThrowsAsync<InvalidOperationException>(() => query.ToListAsync())
            : Assert.Throws<InvalidOperationException>(() => query.ToList());

        Assert.Equal(expectedMessage, exception.Message);
    }
}

/// <summary>
/// Runs scalar and unmapped-type SQL composition through MySqlConnector parameters.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class SqlQueryMySqlTest : SqlQueryTestBase<NorthwindQueryMySqlFixture<NoopModelCustomizer>>
{
    public SqlQueryMySqlTest(
        NorthwindQueryMySqlFixture<NoopModelCustomizer> fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    protected override DbParameter CreateDbParameter(
        string name,
        object value
    ) => new MySqlParameter
    {
        ParameterName = name,
        Value = value,
    };
}

/// <summary>
/// Verifies raw stored-procedure query execution through MySQL's CALL syntax.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class
    FromSqlSprocQueryMySqlTest : FromSqlSprocQueryTestBase<NorthwindQueryMySqlFixture<NoopModelCustomizer>>
{
    public FromSqlSprocQueryMySqlTest(
        NorthwindQueryMySqlFixture<NoopModelCustomizer> fixture
    ) : base(fixture)
    {
        fixture.TestSqlLoggerFactory.Clear();
    }

    protected override string TenMostExpensiveProductsSproc => "CALL `Ten Most Expensive Products`();";

    protected override string CustomerOrderHistorySproc => "CALL `CustOrderHist`({0});";
}

/// <summary>
/// Verifies ExecuteSql and SqlQuery command execution, including stored procedures.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class SqlExecutorMySqlTest : SqlExecutorTestBase<NorthwindQueryMySqlFixture<SqlExecutorModelCustomizer>>
{
    public SqlExecutorMySqlTest(
        NorthwindQueryMySqlFixture<SqlExecutorModelCustomizer> fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    protected override DbParameter CreateDbParameter(
        string name,
        object value
    ) => new MySqlParameter
    {
        ParameterName = name,
        Value = value,
    };

    protected override string TenMostExpensiveProductsSproc => "CALL `Ten Most Expensive Products`();";

    protected override string CustomerOrderHistorySproc => "CALL `CustOrderHist`(@CustomerID);";

    protected override string CustomerOrderHistoryWithGeneratedParameterSproc => "CALL `CustOrderHist`({0});";

    public override async Task Executes_stored_procedure(
        bool async
    )
    {
        using var context = CreateContext();

        Assert.Equal(
            0,
            async
                ? await context.Database.ExecuteSqlRawAsync(TenMostExpensiveProductsSproc)
                : context.Database.ExecuteSqlRaw(TenMostExpensiveProductsSproc));
    }

    public override async Task Executes_stored_procedure_with_parameter(
        bool async
    )
    {
        using var context = CreateContext();
        var parameter = CreateDbParameter("@CustomerID", "ALFKI");

        Assert.Equal(
            0,
            async
                ? await context.Database.ExecuteSqlRawAsync(CustomerOrderHistorySproc, parameter)
                : context.Database.ExecuteSqlRaw(CustomerOrderHistorySproc, parameter));
    }

    public override async Task Executes_stored_procedure_with_generated_parameter(
        bool async
    )
    {
        using var context = CreateContext();

        Assert.Equal(
            0,
            async
                ? await context.Database.ExecuteSqlRawAsync(CustomerOrderHistoryWithGeneratedParameterSproc, "ALFKI")
                : context.Database.ExecuteSqlRaw(CustomerOrderHistoryWithGeneratedParameterSproc, "ALFKI"));
    }

    public override async Task Query_with_parameters(
        bool async
    )
    {
        var city = "London";
        var contactTitle = "Sales Representative";

        using var context = CreateContext();

        var actual = async
            ? await context.Database.ExecuteSqlRawAsync(
                "SELECT COUNT(*) FROM `Customers` " + "WHERE `City` = {0} AND `ContactTitle` = {1}",
                city,
                contactTitle)
            : context.Database.ExecuteSqlRaw(
                "SELECT COUNT(*) FROM `Customers` " + "WHERE `City` = {0} AND `ContactTitle` = {1}",
                city,
                contactTitle);

        Assert.Equal(-1, actual);
    }

    public override async Task Query_with_dbParameter_with_name(
        bool async
    )
    {
        var city = CreateDbParameter("@city", "London");

        using var context = CreateContext();

        var actual = async
            ? await context.Database.ExecuteSqlRawAsync("SELECT COUNT(*) FROM `Customers` WHERE `City` = @city", city)
            : context.Database.ExecuteSqlRaw("SELECT COUNT(*) FROM `Customers` WHERE `City` = @city", city);

        Assert.Equal(-1, actual);
    }

    public override async Task Query_with_positional_dbParameter_with_name(
        bool async
    )
    {
        var city = CreateDbParameter("@city", "London");

        using var context = CreateContext();

        var actual = async
            ? await context.Database.ExecuteSqlRawAsync("SELECT COUNT(*) FROM `Customers` WHERE `City` = {0}", city)
            : context.Database.ExecuteSqlRaw("SELECT COUNT(*) FROM `Customers` WHERE `City` = {0}", city);

        Assert.Equal(-1, actual);
    }

    public override async Task Query_with_positional_dbParameter_without_name(
        bool async
    )
    {
        var city = CreateDbParameter(name: null!, value: "London");

        using var context = CreateContext();

        var actual = async
            ? await context.Database.ExecuteSqlRawAsync("SELECT COUNT(*) FROM `Customers` WHERE `City` = {0}", city)
            : context.Database.ExecuteSqlRaw("SELECT COUNT(*) FROM `Customers` WHERE `City` = {0}", city);

        Assert.Equal(-1, actual);
    }

    public override async Task Query_with_dbParameters_mixed(
        bool async
    )
    {
        var city = "London";
        var contactTitle = "Sales Representative";
        var cityParameter = CreateDbParameter("@city", city);
        var contactTitleParameter = CreateDbParameter("@contactTitle", contactTitle);

        using var context = CreateContext();

        var actual = async
            ? await context.Database.ExecuteSqlRawAsync(
                "SELECT COUNT(*) FROM `Customers` " + "WHERE `City` = {0} AND `ContactTitle` = @contactTitle",
                city,
                contactTitleParameter)
            : context.Database.ExecuteSqlRaw(
                "SELECT COUNT(*) FROM `Customers` " + "WHERE `City` = {0} AND `ContactTitle` = @contactTitle",
                city,
                contactTitleParameter);

        Assert.Equal(-1, actual);

        actual = async
            ? await context.Database.ExecuteSqlRawAsync(
                "SELECT COUNT(*) FROM `Customers` " + "WHERE `City` = @city AND `ContactTitle` = {1}",
                cityParameter,
                contactTitle)
            : context.Database.ExecuteSqlRaw(
                "SELECT COUNT(*) FROM `Customers` " + "WHERE `City` = @city AND `ContactTitle` = {1}",
                cityParameter,
                contactTitle);

        Assert.Equal(-1, actual);
    }

    public override async Task Query_with_parameters_interpolated(
        bool async
    )
    {
        var city = "London";
        var contactTitle = "Sales Representative";

        using var context = CreateContext();
        FormattableString command = $"""
                                     SELECT COUNT(*) FROM `Customers`
                                     WHERE `City` = {city} AND `ContactTitle` = {contactTitle}
                                     """;

        var actual = async
            ? await context.Database.ExecuteSqlInterpolatedAsync(command)
            : context.Database.ExecuteSqlInterpolated(command);

        Assert.Equal(-1, actual);
    }

    public override async Task Query_with_DbParameters_interpolated(
        bool async
    )
    {
        var city = CreateDbParameter("city", "London");
        var contactTitle = CreateDbParameter("contactTitle", "Sales Representative");

        using var context = CreateContext();
        FormattableString command = $"""
                                     SELECT COUNT(*) FROM `Customers`
                                     WHERE `City` = {city} AND `ContactTitle` = {contactTitle}
                                     """;

        var actual = async
            ? await context.Database.ExecuteSqlInterpolatedAsync(command)
            : context.Database.ExecuteSqlInterpolated(command);

        Assert.Equal(-1, actual);
    }

    public override async Task Query_with_parameters_interpolated_2(
        bool async
    )
    {
        var city = "London";
        var contactTitle = "Sales Representative";

        using var context = CreateContext();
        FormattableString command = $"""
                                     SELECT COUNT(*) FROM `Customers`
                                     WHERE `City` = {city} AND `ContactTitle` = {contactTitle}
                                     """;

        var actual = async ? await context.Database.ExecuteSqlAsync(command) : context.Database.ExecuteSql(command);

        Assert.Equal(-1, actual);
    }

    public override async Task Query_with_DbParameters_interpolated_2(
        bool async
    )
    {
        var city = CreateDbParameter("city", "London");
        var contactTitle = CreateDbParameter("contactTitle", "Sales Representative");

        using var context = CreateContext();
        FormattableString command = $"""
                                     SELECT COUNT(*) FROM `Customers`
                                     WHERE `City` = {city} AND `ContactTitle` = {contactTitle}
                                     """;

        var actual = async ? await context.Database.ExecuteSqlAsync(command) : context.Database.ExecuteSql(command);

        Assert.Equal(-1, actual);
    }

    public override async Task Query_with_parameters_custom_converter(
        bool async
    )
    {
        var city = new City
        {
            Name = "London",
        };

        var contactTitle = "Sales Representative";

        using var context = CreateContext();
        FormattableString command = $"""
                                     SELECT COUNT(*) FROM `Customers`
                                     WHERE `City` = {city} AND `ContactTitle` = {contactTitle}
                                     """;

        var actual = async ? await context.Database.ExecuteSqlAsync(command) : context.Database.ExecuteSql(command);

        Assert.Equal(-1, actual);
    }
}

/// <summary>
/// Ensures unsupported client evaluation is rejected after provider translation.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class
    QueryNoClientEvalMySqlTest : QueryNoClientEvalTestBase<NorthwindQueryMySqlFixture<NoopModelCustomizer>>
{
    public QueryNoClientEvalMySqlTest(
        NorthwindQueryMySqlFixture<NoopModelCustomizer> fixture
    ) : base(fixture) { }
}

/// <summary>
/// Verifies the relational warnings emitted by provider query compilation.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class WarningsMySqlTest : WarningsTestBase<NorthwindQueryMySqlFixture<NoopModelCustomizer>>
{
    public WarningsMySqlTest(
        NorthwindQueryMySqlFixture<NoopModelCustomizer> fixture
    ) : base(fixture) { }
}
