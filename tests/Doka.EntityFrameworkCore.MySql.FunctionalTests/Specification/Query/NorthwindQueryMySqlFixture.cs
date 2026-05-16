using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Microsoft.EntityFrameworkCore.Query;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query;

/// <summary>
/// Northwind-query fixture parameterized on the standard EF Core <see cref="ITestModelCustomizer"/>
/// surface. Routes the spec base class to <see cref="MySqlNorthwindTestStoreFactory"/> and turns on
/// detailed-errors so spec-test failures carry the SQL the provider emitted.
/// </summary>
public class NorthwindQueryMySqlFixture<TModelCustomizer> : NorthwindQueryRelationalFixture<TModelCustomizer>
    where TModelCustomizer : ITestModelCustomizer, new()
{
    protected override ITestStoreFactory TestStoreFactory => MySqlNorthwindTestStoreFactory.Instance;

    public override DbContextOptionsBuilder AddOptions(
        DbContextOptionsBuilder builder
    ) => base
        .AddOptions(builder)
        .EnableDetailedErrors();

    protected override bool ShouldLogCategory(
        string logCategory
    ) => logCategory == DbLoggerCategory.Query.Name
        || logCategory == DbLoggerCategory.Database.Command.Name;
}
