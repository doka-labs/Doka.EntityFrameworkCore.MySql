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
    public NorthwindWhereQueryMySqlTest(
        NorthwindQueryMySqlFixture<NoopModelCustomizer> fixture
    ) : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
    }
}
