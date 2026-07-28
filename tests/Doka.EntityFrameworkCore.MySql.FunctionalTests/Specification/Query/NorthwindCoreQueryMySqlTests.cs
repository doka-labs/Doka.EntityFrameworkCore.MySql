using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query;

/// <summary>
/// Executes the official no-tracking Northwind query contract on the provider.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public class
    NorthwindAsNoTrackingQueryMySqlTest : NorthwindAsNoTrackingQueryTestBase<
    NorthwindQueryMySqlFixture<NoopModelCustomizer>>
{
    public NorthwindAsNoTrackingQueryMySqlTest(
        NorthwindQueryMySqlFixture<NoopModelCustomizer> fixture
    ) : base(fixture) { }
}

/// <summary>
/// Executes the official tracking Northwind query contract on the provider.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public class
    NorthwindAsTrackingQueryMySqlTest : NorthwindAsTrackingQueryTestBase<
    NorthwindQueryMySqlFixture<NoopModelCustomizer>>
{
    public NorthwindAsTrackingQueryMySqlTest(
        NorthwindQueryMySqlFixture<NoopModelCustomizer> fixture
    ) : base(fixture) { }
}

/// <summary>
/// Executes the official Northwind change-tracking query contract on the provider.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public class
    NorthwindChangeTrackingQueryMySqlTest : NorthwindChangeTrackingQueryTestBase<
    NorthwindQueryMySqlFixture<NoopModelCustomizer>>
{
    public NorthwindChangeTrackingQueryMySqlTest(
        NorthwindQueryMySqlFixture<NoopModelCustomizer> fixture
    ) : base(fixture) { }
}

/// <summary>
/// Executes the official compiled Northwind query contract on the provider.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public class
    NorthwindCompiledQueryMySqlTest : NorthwindCompiledQueryTestBase<NorthwindQueryMySqlFixture<NoopModelCustomizer>>
{
    public NorthwindCompiledQueryMySqlTest(
        NorthwindQueryMySqlFixture<NoopModelCustomizer> fixture
    ) : base(fixture) { }
}

/// <summary>
/// Executes the official Northwind query-filter contract with its dedicated model customizer.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public class NorthwindQueryFiltersQueryMySqlTest : NorthwindQueryFiltersQueryTestBase<
    NorthwindQueryMySqlFixture<NorthwindQueryFiltersCustomizer>>
{
    public NorthwindQueryFiltersQueryMySqlTest(
        NorthwindQueryMySqlFixture<NorthwindQueryFiltersCustomizer> fixture
    ) : base(fixture) { }
}

/// <summary>
/// Executes the official Northwind query-tagging contract on the provider.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public class
    NorthwindQueryTaggingQueryMySqlTest : NorthwindQueryTaggingQueryTestBase<
    NorthwindQueryMySqlFixture<NoopModelCustomizer>>
{
    public NorthwindQueryTaggingQueryMySqlTest(
        NorthwindQueryMySqlFixture<NoopModelCustomizer> fixture
    ) : base(fixture) { }
}
