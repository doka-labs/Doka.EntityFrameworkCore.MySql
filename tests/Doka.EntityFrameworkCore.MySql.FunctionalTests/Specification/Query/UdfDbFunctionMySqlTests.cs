using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Xunit.Abstractions;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query;

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class UdfDbFunctionMySqlTest : UdfDbFunctionTestBase<UdfDbFunctionMySqlTest.MySqlUdfFixture>
{
    public UdfDbFunctionMySqlTest(
        MySqlUdfFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    [SpecEngineLimitationFact("MYSQL-MARIADB-SCALAR-STORED-FUNCTIONS", "mysql84", "mariadb114", "mariadb118")]
    public override void QF_Correlated_Select_In_Anonymous() => base.QF_Correlated_Select_In_Anonymous();

    [SpecEngineLimitationFact("MYSQL-MARIADB-SCALAR-STORED-FUNCTIONS", "mysql84", "mariadb114", "mariadb118")]
    public override void QF_Correlated_Nested_Func_Call() => base.QF_Correlated_Nested_Func_Call();

    [SpecEngineLimitationFact("MYSQL-MARIADB-SCALAR-STORED-FUNCTIONS", "mysql84", "mariadb114", "mariadb118")]
    public override void DbSet_mapped_to_function() => base.DbSet_mapped_to_function();

    [SpecEngineLimitationFact("MYSQL-MARIADB-SCALAR-STORED-FUNCTIONS", "mysql84", "mariadb114", "mariadb118")]
    public override void QF_Join() => base.QF_Join();

    [SpecEngineLimitationFact("MYSQL-MARIADB-SCALAR-STORED-FUNCTIONS", "mysql84", "mariadb114", "mariadb118")]
    public override void QF_Stand_Alone() => base.QF_Stand_Alone();

    [SpecEngineLimitationFact("MYSQL-MARIADB-SCALAR-STORED-FUNCTIONS", "mysql84", "mariadb114", "mariadb118")]
    public override void QF_OuterApply_Correlated_Select_QF() => base.QF_OuterApply_Correlated_Select_QF();

    [SpecEngineLimitationFact("MYSQL-MARIADB-SCALAR-STORED-FUNCTIONS", "mysql84", "mariadb114", "mariadb118")]
    public override void QF_OuterApply_Correlated_Select_Entity() => base.QF_OuterApply_Correlated_Select_Entity();

    [SpecEngineLimitationFact("MYSQL-MARIADB-SCALAR-STORED-FUNCTIONS", "mysql84", "mariadb114", "mariadb118")]
    public override void QF_OuterApply_Correlated_Select_Anonymous() =>
        base.QF_OuterApply_Correlated_Select_Anonymous();

    [SpecEngineLimitationFact("MYSQL-MARIADB-SCALAR-STORED-FUNCTIONS", "mysql84", "mariadb114", "mariadb118")]
    public override void Udf_with_argument_being_comparison_to_null_parameter() =>
        base.Udf_with_argument_being_comparison_to_null_parameter();

    [SpecEngineLimitationFact("MYSQL-MARIADB-SCALAR-STORED-FUNCTIONS", "mysql84", "mariadb114", "mariadb118")]
    public override void Udf_with_argument_being_comparison_of_nullable_columns() =>
        base.Udf_with_argument_being_comparison_of_nullable_columns();

    [SpecEngineLimitationFact("MYSQL-MARIADB-SCALAR-STORED-FUNCTIONS", "mysql84", "mariadb114", "mariadb118")]
    public override void TVF_with_navigation_in_projection_groupby_aggregate() =>
        base.TVF_with_navigation_in_projection_groupby_aggregate();

    [SpecEngineLimitationFact("MYSQL-MARIADB-SCALAR-STORED-FUNCTIONS", "mysql84", "mariadb114", "mariadb118")]
    public override void TVF_with_argument_being_a_subquery_with_navigation_in_projection_groupby_aggregate() =>
        base.TVF_with_argument_being_a_subquery_with_navigation_in_projection_groupby_aggregate();

    [SpecEngineLimitationFact("MYSQL-MARIADB-SCALAR-STORED-FUNCTIONS", "mysql84", "mariadb114", "mariadb118")]
    public override void QF_LeftJoin_Select_Anonymous() => base.QF_LeftJoin_Select_Anonymous();

    [SpecEngineLimitationFact("MYSQL-MARIADB-SCALAR-STORED-FUNCTIONS", "mysql84", "mariadb114", "mariadb118")]
    public override void QF_CrossApply_Correlated_Select_QF_Type() => base.QF_CrossApply_Correlated_Select_QF_Type();

    [SpecEngineLimitationFact("MYSQL-MARIADB-SCALAR-STORED-FUNCTIONS", "mysql84", "mariadb114", "mariadb118")]
    public override void QF_Correlated_Func_Call_With_Navigation() => base.QF_Correlated_Func_Call_With_Navigation();

    [SpecEngineLimitationFact("MYSQL-MARIADB-SCALAR-STORED-FUNCTIONS", "mysql84", "mariadb114", "mariadb118")]
    public override void QF_Stand_Alone_Parameter() => base.QF_Stand_Alone_Parameter();

    [SpecEngineLimitationFact("MYSQL-MARIADB-SCALAR-STORED-FUNCTIONS", "mysql84", "mariadb114", "mariadb118")]
    public override void QF_CrossJoin_Parameter() => base.QF_CrossJoin_Parameter();

    [SpecEngineLimitationFact("MYSQL-MARIADB-SCALAR-STORED-FUNCTIONS", "mysql84", "mariadb114", "mariadb118")]
    public override void QF_LeftJoin_Select_Result() => base.QF_LeftJoin_Select_Result();

    [SpecEngineLimitationFact("MYSQL-MARIADB-SCALAR-STORED-FUNCTIONS", "mysql84", "mariadb114", "mariadb118")]
    public override void QF_Select_Correlated_Direct_With_Function_Query_Parameter_Correlated_In_Anonymous() =>
        base.QF_Select_Correlated_Direct_With_Function_Query_Parameter_Correlated_In_Anonymous();

    [SpecEngineLimitationFact("MYSQL-MARIADB-SCALAR-STORED-FUNCTIONS", "mysql84", "mariadb114", "mariadb118")]
    public override void QF_CrossJoin_Not_Correlated() => base.QF_CrossJoin_Not_Correlated();

    [SpecEngineLimitationFact("MYSQL-MARIADB-SCALAR-STORED-FUNCTIONS", "mysql84", "mariadb114", "mariadb118")]
    public override void QF_Select_Correlated_Subquery_In_Anonymous() =>
        base.QF_Select_Correlated_Subquery_In_Anonymous();

    [SpecEngineLimitationFact("MYSQL-MARIADB-SCALAR-STORED-FUNCTIONS", "mysql84", "mariadb114", "mariadb118")]
    public override void QF_Nested() => base.QF_Nested();

    [SpecEngineLimitationFact("MYSQL-MARIADB-SCALAR-STORED-FUNCTIONS", "mysql84", "mariadb114", "mariadb118")]
    public override void QF_Select_Correlated_Subquery_In_Anonymous_Nested_With_QF() =>
        base.QF_Select_Correlated_Subquery_In_Anonymous_Nested_With_QF();

    [SpecEngineLimitationFact("MYSQL-MARIADB-SCALAR-STORED-FUNCTIONS", "mysql84", "mariadb114", "mariadb118")]
    public override void QF_CrossApply_Correlated_Select_Anonymous() =>
        base.QF_CrossApply_Correlated_Select_Anonymous();

    [SpecEngineLimitationFact("MYSQL-MARIADB-SCALAR-STORED-FUNCTIONS", "mysql84", "mariadb114", "mariadb118")]
    public override void QF_CrossApply_Correlated_Select_Result() => base.QF_CrossApply_Correlated_Select_Result();

    public sealed class MySqlUdfFixture : UdfFixtureBase
    {
        protected override string StoreName => "UDFDbFunctionMySqlTests";

        protected override Type ContextType { get; } = typeof(MySqlUdfContext);

        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

        protected override async Task SeedAsync(
            DbContext context
        )
        {
            await base.SeedAsync(context);
            await context.SaveChangesAsync();

            foreach (var commandText in s_dropFunctionCommands)
            {
                await context.Database.ExecuteSqlRawAsync(commandText);
            }

            foreach (var commandText in s_createFunctionCommands)
            {
                await context.Database.ExecuteSqlRawAsync(commandText);
            }
        }

        private static readonly string[] s_dropFunctionCommands =
        [
            "DROP FUNCTION IF EXISTS `AddValues`",
            "DROP FUNCTION IF EXISTS `CustomerOrderCount`",
            "DROP FUNCTION IF EXISTS `DollarValue`",
            "DROP FUNCTION IF EXISTS `GetCustomerWithMostOrdersAfterDate`",
            "DROP FUNCTION IF EXISTS `GetReportingPeriodStartDate`",
            "DROP FUNCTION IF EXISTS `IdentityString`",
            "DROP FUNCTION IF EXISTS `IdentityStringNonNullable`",
            "DROP FUNCTION IF EXISTS `IdentityStringNonNullableFluent`",
            "DROP FUNCTION IF EXISTS `IdentityStringPropagateNull`",
            "DROP FUNCTION IF EXISTS `IsDate`",
            "DROP FUNCTION IF EXISTS `IsTopCustomer`",
            "DROP FUNCTION IF EXISTS `len`",
            "DROP FUNCTION IF EXISTS `StarValue`",
            "DROP FUNCTION IF EXISTS `StringLength`",
        ];

        private static readonly string[] s_createFunctionCommands =
        [
            """
            CREATE FUNCTION `AddValues` (`leftValue` int, `rightValue` int)
            RETURNS int DETERMINISTIC
            RETURN `leftValue` + `rightValue`
            """,
            """
            CREATE FUNCTION `CustomerOrderCount` (`requestedCustomerId` int)
            RETURNS int READS SQL DATA
            RETURN (
                SELECT COUNT(*)
                FROM `Orders`
                WHERE `CustomerId` = `requestedCustomerId`)
            """,
            """
            CREATE FUNCTION `DollarValue` (`dollarCount` int, `value` longtext)
            RETURNS longtext DETERMINISTIC
            RETURN CONCAT(REPEAT('$', `dollarCount`), `value`)
            """,
            """
            CREATE FUNCTION `GetCustomerWithMostOrdersAfterDate` (`requestedDate` datetime)
            RETURNS int READS SQL DATA
            RETURN (
                SELECT `CustomerId`
                FROM `Orders`
                WHERE `OrderDate` > `requestedDate`
                GROUP BY `CustomerId`
                ORDER BY COUNT(*) DESC
                LIMIT 1)
            """,
            """
            CREATE FUNCTION `GetReportingPeriodStartDate` (`period` int)
            RETURNS datetime DETERMINISTIC
            RETURN CAST('1998-01-01 00:00:00' AS datetime)
            """,
            """
            CREATE FUNCTION `IdentityString` (`value` longtext)
            RETURNS longtext DETERMINISTIC
            RETURN `value`
            """,
            """
            CREATE FUNCTION `IdentityStringNonNullable` (`value` longtext)
            RETURNS longtext DETERMINISTIC
            RETURN COALESCE(`value`, 'NULL')
            """,
            """
            CREATE FUNCTION `IdentityStringNonNullableFluent` (`value` longtext)
            RETURNS longtext DETERMINISTIC
            RETURN COALESCE(`value`, 'NULL')
            """,
            """
            CREATE FUNCTION `IdentityStringPropagateNull` (`value` longtext)
            RETURNS longtext DETERMINISTIC
            RETURN `value`
            """,
            """
            CREATE FUNCTION `IsDate` (`value` longtext)
            RETURNS boolean DETERMINISTIC
            RETURN `value` REGEXP
                '^(1000|[1-9][0-9]{{3}})-(0[1-9]|1[0-2])-(0[1-9]|[12][0-9]|3[01])$'
            """,
            """
            CREATE FUNCTION `IsTopCustomer` (`customerId` int)
            RETURNS boolean DETERMINISTIC
            RETURN `customerId` = 1
            """,
            """
            CREATE FUNCTION `len` (`value` longtext)
            RETURNS bigint DETERMINISTIC
            RETURN CHAR_LENGTH(`value`)
            """,
            """
            CREATE FUNCTION `StarValue` (`starCount` int, `value` int)
            RETURNS longtext DETERMINISTIC
            RETURN CONCAT(REPEAT('*', `starCount`), `value`)
            """,
            """
            CREATE FUNCTION `StringLength` (`value` longtext)
            RETURNS int DETERMINISTIC
            RETURN CHAR_LENGTH(`value`)
            """,
        ];
    }

    private sealed class MySqlUdfContext : UDFSqlContext
    {
        public MySqlUdfContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder
                .HasDbFunction(typeof(UDFSqlContext).GetMethod(nameof(IdentityString), [typeof(string)])!)
                .HasSchema(null);
        }
    }
}
