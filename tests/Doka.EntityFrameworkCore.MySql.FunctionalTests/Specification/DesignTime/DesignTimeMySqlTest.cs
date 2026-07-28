using System.Reflection;
using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.DesignTime;

/// <summary>
/// Verifies that EF Core discovers the provider's design-time services from its assembly
/// metadata and can resolve the reverse-engineering and migrations service graphs.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public class DesignTimeMySqlTest : DesignTimeTestBase<DesignTimeMySqlTest.DesignTimeMySqlFixture>
{
    public DesignTimeMySqlTest(
        DesignTimeMySqlFixture fixture
    ) : base(fixture)
    {
    }

    protected override Assembly ProviderAssembly => typeof(MySqlDesignTimeServices).Assembly;

    public sealed class DesignTimeMySqlFixture : DesignTimeFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
    }
}
