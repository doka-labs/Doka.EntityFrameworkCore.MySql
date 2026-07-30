using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.CrossCutting.Interception;

/// <summary>
/// Verifies query-expression interceptors registered through dependency injection.
/// </summary>
public abstract class QueryExpressionInterceptionMySqlTestBase : QueryExpressionInterceptionTestBase
{
    protected QueryExpressionInterceptionMySqlTestBase(
        InterceptionMySqlFixtureBase fixture
    ) : base(fixture) { }

    public abstract class InterceptionMySqlFixtureBase : InterceptionFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

        protected override IServiceCollection InjectInterceptors(
            IServiceCollection serviceCollection,
            IEnumerable<IInterceptor> injectedInterceptors
        ) => base.InjectInterceptors(serviceCollection.AddEntityFrameworkDokaMySql(), injectedInterceptors);
    }
}

/// <summary>
/// Exercises query-expression interceptors without diagnostic-listener subscriptions.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class QueryExpressionInterceptionMySqlTest : QueryExpressionInterceptionMySqlTestBase,
    IClassFixture<QueryExpressionInterceptionMySqlTest.MySqlFixture>
{
    public QueryExpressionInterceptionMySqlTest(
        MySqlFixture fixture
    ) : base(fixture) { }

    public sealed class MySqlFixture : InterceptionMySqlFixtureBase
    {
        protected override string StoreName => "QueryExpressionInterception";

        protected override bool ShouldSubscribeToDiagnosticListener => false;
    }
}

/// <summary>
/// Exercises query-expression interceptors with diagnostic-listener subscriptions.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class QueryExpressionInterceptionWithDiagnosticsMySqlTest : QueryExpressionInterceptionMySqlTestBase,
    IClassFixture<QueryExpressionInterceptionWithDiagnosticsMySqlTest.MySqlFixture>
{
    public QueryExpressionInterceptionWithDiagnosticsMySqlTest(
        MySqlFixture fixture
    ) : base(fixture) { }

    public sealed class MySqlFixture : InterceptionMySqlFixtureBase
    {
        protected override string StoreName => "QueryExpressionInterceptionWithDiagnostics";

        protected override bool ShouldSubscribeToDiagnosticListener => true;
    }
}

/// <summary>
/// Runs singleton-interceptor and materialization-interceptor contracts against JSON-owned data.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class MaterializationInterceptionMySqlTest : MaterializationInterceptionTestBase<
    MaterializationInterceptionMySqlTest.MySqlLibraryContext>
{
    public MaterializationInterceptionMySqlTest(
        NonSharedFixture fixture
    ) : base(fixture) { }

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

    public sealed class MySqlLibraryContext : LibraryContext
    {
        public MySqlLibraryContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder
                .Entity<TestEntity30244>()
                .OwnsMany(e => e.Settings, owned => owned.ToJson());
        }
    }
}
