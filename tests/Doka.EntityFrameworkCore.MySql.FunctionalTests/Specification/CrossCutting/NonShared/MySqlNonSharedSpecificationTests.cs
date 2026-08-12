using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.CrossCutting.NonShared;

/// <summary>
/// Exercises ad-hoc many-to-many models whose shape is built independently per test.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class AdHocManyToManyQueryMySqlTest : AdHocManyToManyQueryRelationalTestBase
{
    public AdHocManyToManyQueryMySqlTest(
        NonSharedFixture fixture
    ) : base(fixture) { }

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}

/// <summary>
/// Runs relational owned-entity queries over independently constructed models.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class OwnedEntityQueryMySqlTest : OwnedEntityQueryRelationalTestBase
{
    public OwnedEntityQueryMySqlTest(
        NonSharedFixture fixture
    ) : base(fixture) { }

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

    /// <summary>
    /// Verifies null comparison for an owned navigation in a correlated
    /// collection while making the upstream assertion order deterministic.
    /// </summary>
    public override async Task Correlated_subquery_with_owned_navigation_being_compared_to_null_works()
    {
        var contextFactory = await InitializeAsync<Context13157>(seed: context => context.SeedAsync());

        using var context = contextFactory.CreateContext();

        // The upstream test asserts collection positions but does not order
        // its relational query. Order by the child key so the assertion keeps
        // testing owned-navigation null semantics rather than storage order.
        var query = context.Partners
            .Select(partner => new
            {
                Addresses = partner.Addresses
                    .OrderBy(address => address.Id)
                    .Select(address => new
                    {
                        Turnovers = address.Turnovers == null
                            ? null
                            : new { address.Turnovers.AmountIn },
                    })
                    .ToList(),
            });

        Assert.Contains(
            "ORDER BY `p`.`Id`, `a`.`Id`",
            query.ToQueryString(),
            StringComparison.Ordinal);

        var partners = query.ToList();

        Assert.Single(partners);
        Assert.Collection(
            partners[0].Addresses,
            address =>
            {
                Assert.NotNull(address.Turnovers);
                Assert.Equal(10, address.Turnovers.AmountIn);
            },
            address => Assert.Null(address.Turnovers));
    }
}

/// <summary>
/// Runs shared-type entity queries over independently constructed relational models.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class SharedTypeQueryMySqlTest : SharedTypeQueryRelationalTestBase
{
    public SharedTypeQueryMySqlTest(
        NonSharedFixture fixture
    ) : base(fixture) { }

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;
}
