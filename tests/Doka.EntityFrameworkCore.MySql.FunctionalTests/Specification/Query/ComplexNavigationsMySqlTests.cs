using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query.Fixtures;
using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Microsoft.EntityFrameworkCore.TestModels.ComplexNavigationsModel;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query;

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class
    ComplexNavigationsQueryMySqlTest : ComplexNavigationsQueryRelationalTestBase<ComplexNavigationsMySqlFixture>
{
    public ComplexNavigationsQueryMySqlTest(
        ComplexNavigationsMySqlFixture fixture
    ) : base(fixture) { }

    /// <summary>
    /// Verifies the ordered-parent projection without assigning an order to the
    /// selected parent's child collection.
    /// </summary>
    [DirectTheory]
    [InheritedTheoryData]
    public override Task SelectMany_subquery_with_custom_projection(
        bool async
    ) => ComplexNavigationContractAssertions.AssertSelectManySubqueryWithCustomProjection(
        async,
        () => Fixture.CreateContext(),
        Fixture.GetExpectedData(),
        cancellationToken: CancellationToken.None);

    public override async Task GroupJoin_client_method_in_OrderBy(
        bool async
    )
    {
        await ComplexNavigationContractAssertions.AssertClientOrderByTranslationFails(
            () => base.GroupJoin_client_method_in_OrderBy(async),
            typeof(ComplexNavigationsMySqlFixture),
            cancellationToken: CancellationToken.None);
    }

    public override async Task Join_with_result_selector_returning_queryable_throws_validation_error(
        bool async
    )
    {
        await ComplexNavigationContractAssertions.AssertQueryableResultValidationFails(
            () => base.Join_with_result_selector_returning_queryable_throws_validation_error(async),
            cancellationToken: CancellationToken.None);
    }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class
    ComplexNavigationsCollectionsQueryMySqlTest : ComplexNavigationsCollectionsQueryRelationalTestBase<
    ComplexNavigationsMySqlFixture>
{
    public ComplexNavigationsCollectionsQueryMySqlTest(
        ComplexNavigationsMySqlFixture fixture
    ) : base(fixture) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class
    ComplexNavigationsCollectionsSplitQueryMySqlTest : ComplexNavigationsCollectionsSplitQueryRelationalTestBase<
    ComplexNavigationsMySqlFixture>
{
    public ComplexNavigationsCollectionsSplitQueryMySqlTest(
        ComplexNavigationsMySqlFixture fixture
    ) : base(fixture) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class
    ComplexNavigationsSharedTypeQueryMySqlTest : ComplexNavigationsSharedTypeQueryRelationalTestBase<
    ComplexNavigationsSharedTypeMySqlFixture>
{
    public ComplexNavigationsSharedTypeQueryMySqlTest(
        ComplexNavigationsSharedTypeMySqlFixture fixture
    ) : base(fixture) { }

    /// <summary>
    /// Verifies the ordered-parent projection without assigning an order to the
    /// selected parent's child collection.
    /// </summary>
    [DirectTheory]
    [InheritedTheoryData]
    public override Task SelectMany_subquery_with_custom_projection(
        bool async
    ) => ComplexNavigationContractAssertions.AssertSelectManySubqueryWithCustomProjection(
        async,
        () => Fixture.CreateContext(),
        Fixture.GetExpectedData(),
        cancellationToken: CancellationToken.None);

    public override async Task GroupJoin_client_method_in_OrderBy(
        bool async
    )
    {
        await ComplexNavigationContractAssertions.AssertClientOrderByTranslationFails(
            () => base.GroupJoin_client_method_in_OrderBy(async),
            typeof(ComplexNavigationsSharedTypeMySqlFixture),
            cancellationToken: CancellationToken.None);
    }

    public override async Task Join_with_result_selector_returning_queryable_throws_validation_error(
        bool async
    )
    {
        await ComplexNavigationContractAssertions.AssertQueryableResultValidationFails(
            () => base.Join_with_result_selector_returning_queryable_throws_validation_error(async),
            cancellationToken: CancellationToken.None);
    }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class ComplexNavigationsCollectionsSharedTypeQueryMySqlTest :
    ComplexNavigationsCollectionsSharedTypeQueryRelationalTestBase<ComplexNavigationsSharedTypeMySqlFixture>
{
    public ComplexNavigationsCollectionsSharedTypeQueryMySqlTest(
        ComplexNavigationsSharedTypeMySqlFixture fixture
    ) : base(fixture) { }
}

[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class ComplexNavigationsCollectionsSplitSharedTypeQueryMySqlTest :
    ComplexNavigationsCollectionsSplitSharedTypeQueryRelationalTestBase<ComplexNavigationsSharedTypeMySqlFixture>
{
    public ComplexNavigationsCollectionsSplitSharedTypeQueryMySqlTest(
        ComplexNavigationsSharedTypeMySqlFixture fixture
    ) : base(fixture) { }
}

/// <summary>
/// Centralizes the upstream relational contract adaptations shared by both model shapes.
/// </summary>
file static class ComplexNavigationContractAssertions
{
    private const string QueryTestBaseTypeName = "Microsoft.EntityFrameworkCore.Query.ComplexNavigationsQueryTestBase";

    /// <summary>
    /// Checks the expected upstream client-ordering translation failure.
    /// </summary>
    /// <param name="cancellationToken">The token observed before invoking the upstream tokenless assertion.</param>
    /// <param name="action">The upstream assertion callback to invoke.</param>
    /// <param name="fixtureType">The upstream fixture type named in translation diagnostics.</param>
    public static async Task AssertClientOrderByTranslationFails(
        Func<Task> action,
        Type fixtureType,
        CancellationToken cancellationToken
    )
    {
        // WHY: The upstream EF assertion callback does not expose a cancellation token.
        cancellationToken.ThrowIfCancellationRequested();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(action);
        var queryTestBaseType = $"{QueryTestBaseTypeName}<{fixtureType.FullName}>";
        var expectedDetails = CoreStrings.QueryUnableToTranslateMethod(queryTestBaseType, "ClientMethodNullableInt");

        Assert.Contains(expectedDetails, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Checks the expected upstream queryable-result validation failure.
    /// </summary>
    /// <param name="cancellationToken">The token observed before invoking the upstream tokenless assertion.</param>
    /// <param name="action">The upstream assertion callback to invoke.</param>
    public static async Task AssertQueryableResultValidationFails(
        Func<Task> action,
        CancellationToken cancellationToken
    )
    {
        // WHY: The upstream EF assertion callback does not expose a cancellation token.
        cancellationToken.ThrowIfCancellationRequested();

        // Materializing IQueryable<T> inside a result projection is invalid. EF Core
        // currently reports that validation failure while creating the query executor.
        await Assert.ThrowsAsync<ArgumentException>(action);
    }

    /// <summary>
    /// Verifies the original custom-projection query, its outer-parent ordering, and
    /// the set of valid first-child results without assuming collection order.
    /// </summary>
    /// <remarks>
    /// The upstream query orders <see cref="Level1" /> rows by their unique ID, but it
    /// does not order the children produced by <c>SelectMany</c>. SQL may therefore
    /// return any child of the first parent. Sources retrieved 2026-07-30:
    /// <see href="https://dev.mysql.com/doc/refman/8.4/en/limit-optimization.html">
    /// MySQL LIMIT query optimization</see> and
    /// <see href="https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/order-by">
    /// MariaDB ORDER BY</see>.
    /// </remarks>
    /// <param name="cancellationToken">The token to observe while performing this operation.</param>
    /// <param name="async">Whether the scenario exercises asynchronous execution.</param>
    /// <param name="contextFactory">The factory that creates the context used by the query.</param>
    /// <param name="expectedData">The in-memory source of expected query results.</param>
    public static async Task AssertSelectManySubqueryWithCustomProjection(
        bool async,
        Func<DbContext> contextFactory,
        ISetSource expectedData,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var context = contextFactory();
        var query = context
            .Set<Level1>()
            .OrderBy(level1 => level1.Id)
            .SelectMany(level1 => level1.OneToMany_Optional1.Select(level2 => new { level2.Name }))
            .Take(1);

        var sql = query.ToQueryString();

        Assert.Contains("ORDER BY `l`.`Id`", sql, StringComparison.Ordinal);

        var results = async
            ? await query.ToListAsync(cancellationToken: cancellationToken)
            : query.ToList();

        var result = Assert.Single(results);
        var firstParent = expectedData
            .Set<Level1>()
            .OrderBy(level1 => level1.Id)
            .First();

        var validNames = firstParent.OneToMany_Optional1.Select(level2 => level2.Name);

        Assert.Contains(result.Name, validNames);
    }
}
