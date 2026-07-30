using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.CrossCutting.Fixtures;

/// <summary>
/// Shared relational Formula 1 fixture for cross-cutting tests that exercise the same
/// model through tracking, concurrency, data binding, and serialization entry points.
/// </summary>
public sealed class MySqlF1Fixture : F1RelationalFixture<byte[]>
{
    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

    public override TestHelpers TestHelpers => MySqlTestHelpers.Instance;
}
