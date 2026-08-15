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
    /// Verifies an owned collection projection with deterministic relational ordering.
    /// </summary>
    public override async Task Projecting_correlated_collection_property_for_owned_entity(
        bool async
    )
    {
        var contextFactory = await InitializeAsync<Context18582>(seed: context => context.SeedAsync());

        using var context = contextFactory.CreateContext();

        // The upstream assertion is positional, while SQL without ORDER BY has
        // no row-order contract. Preserve the asserted seed order explicitly.
        var query = context
            .Warehouses.Select(warehouse => new Context18582.WarehouseModel
            {
                WarehouseCode = warehouse.WarehouseCode,
                DestinationCountryCodes = warehouse
                    .DestinationCountries.OrderBy(country => country.Id)
                    .Select(country => country.CountryCode)
                    .ToArray(),
            })
            .AsNoTracking();

        Assert.Contains("ORDER BY `w`.`Id`, `w0`.`Id`", query.ToQueryString(), StringComparison.Ordinal);

        var result = async ? await query.ToListAsync() : query.ToList();

        var warehouseModel = Assert.Single(result);
        Assert.Equal("W001", warehouseModel.WarehouseCode);
        Assert.Equal(["US", "CA"], warehouseModel.DestinationCountryCodes);
    }

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
