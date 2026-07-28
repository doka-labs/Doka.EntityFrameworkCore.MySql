using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Update;

/// <summary>
/// Runs the relational store-generated key fixup contract against MySQL and
/// MariaDB. The inherited cases cover relationship fixup across one-to-one,
/// one-to-many, unidirectional, overlapping, and partially tracked graphs.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class StoreGeneratedFixupMySqlTest
    : StoreGeneratedFixupRelationalTestBase<
        StoreGeneratedFixupMySqlTest.StoreGeneratedFixupMySqlFixture>
{
    public StoreGeneratedFixupMySqlTest(
        StoreGeneratedFixupMySqlFixture fixture
    ) : base(fixture)
    {
    }

    /// <summary>
    /// Verifies that a temporary key can be promoted to a permanent value without
    /// leaking its deterministic test value into subsequent specification runs.
    /// </summary>
    [Fact]
    public Task Temp_values_can_be_made_permanent() =>
        ExecuteWithStrategyInTransactionAsync(
            async context =>
            {
                await context.Set<TestTemp>().ExecuteDeleteAsync();

                var entry = context.Add(new TestTemp());

                Assert.True(entry.Property(e => e.Id).IsTemporary);
                Assert.False(entry.Property(e => e.NotId).IsTemporary);

                var temporaryValue = entry.Property(e => e.Id).CurrentValue;
                entry.Property(e => e.Id).IsTemporary = false;

                await context.SaveChangesAsync();

                Assert.False(entry.Property(e => e.Id).IsTemporary);
                Assert.Equal(temporaryValue, entry.Property(e => e.Id).CurrentValue);
            });

    protected override bool EnforcesFKs => true;

    protected override void UseTransaction(
        DatabaseFacade facade,
        IDbContextTransaction transaction
    ) => facade.UseTransaction(transaction.GetDbTransaction());

    /// <summary>
    /// Routes the official relational fixup model through the provider's real
    /// test-store lifecycle.
    /// </summary>
    public sealed class StoreGeneratedFixupMySqlFixture :
        StoreGeneratedFixupRelationalFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory =>
            MySqlTestStoreFactory.Instance;
    }
}
