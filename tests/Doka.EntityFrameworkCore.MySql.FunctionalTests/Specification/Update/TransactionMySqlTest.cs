using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Update;

/// <summary>
/// Runs the official EF Core transaction contract against the configured MySQL
/// or MariaDB engine. The inherited suite covers implicit, explicit, ambient,
/// and enlisted transactions together with savepoint and connection ownership
/// semantics.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class TransactionMySqlTest
    : TransactionTestBase<TransactionMySqlTest.TransactionMySqlFixture>
{
    public TransactionMySqlTest(
        TransactionMySqlFixture fixture
    ) : base(fixture)
    {
    }

    protected override bool SnapshotSupported => true;

    protected override bool AmbientTransactionsSupported => true;

    protected override DbContext CreateContextWithConnectionString()
    {
        var options = Fixture
            .AddOptions(
                new DbContextOptionsBuilder().UseMySql(
                    TestStore.ConnectionString,
                    MySqlTestEnvironment.ServerVersion))
            .UseInternalServiceProvider(Fixture.ServiceProvider);

        return new DbContext(options.Options);
    }

    /// <summary>
    /// Provides the shared database and deterministic reseed contract required
    /// by the inherited transaction tests.
    /// </summary>
    public sealed class TransactionMySqlFixture : TransactionFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory =>
            MySqlTestStoreFactory.Instance;

        public override DbContextOptionsBuilder AddOptions(
            DbContextOptionsBuilder builder
        ) => base
            .AddOptions(builder)
            .ConfigureWarnings(warnings => warnings.Log(RelationalEventId.MultipleCollectionIncludeWarning));

        public override async Task ReseedAsync()
        {
            await using var context = CreateContext();
            context.Set<TransactionCustomer>().RemoveRange(
                await context.Set<TransactionCustomer>().ToListAsync());
            context.Set<TransactionOrder>().RemoveRange(
                await context.Set<TransactionOrder>().ToListAsync());
            await context.SaveChangesAsync();

            await base.SeedAsync(context);
        }
    }
}
