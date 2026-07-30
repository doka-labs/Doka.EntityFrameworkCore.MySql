using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.CrossCutting.Concurrency;

/// <summary>
/// Verifies that disabling EF Core thread-safety checks changes only concurrency detection,
/// not provider behavior.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class ConcurrencyDetectorDisabledMySqlTest : ConcurrencyDetectorDisabledRelationalTestBase<
    ConcurrencyDetectorDisabledMySqlTest.MySqlFixture>
{
    public ConcurrencyDetectorDisabledMySqlTest(
        MySqlFixture fixture
    ) : base(fixture) { }

    public sealed class MySqlFixture : ConcurrencyDetectorFixtureBase, ITestSqlLoggerFactory
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

        public TestSqlLoggerFactory TestSqlLoggerFactory => (TestSqlLoggerFactory)ListLoggerFactory;

        public override DbContextOptionsBuilder AddOptions(
            DbContextOptionsBuilder builder
        ) => builder.EnableThreadSafetyChecks(enableChecks: false);
    }
}

/// <summary>
/// Verifies the default concurrency-detector behavior through the relational provider stack.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class ConcurrencyDetectorEnabledMySqlTest : ConcurrencyDetectorEnabledRelationalTestBase<
    ConcurrencyDetectorEnabledMySqlTest.MySqlFixture>
{
    public ConcurrencyDetectorEnabledMySqlTest(
        MySqlFixture fixture
    ) : base(fixture) { }

    public sealed class MySqlFixture : ConcurrencyDetectorFixtureBase, ITestSqlLoggerFactory
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

        public TestSqlLoggerFactory TestSqlLoggerFactory => (TestSqlLoggerFactory)ListLoggerFactory;
    }
}
