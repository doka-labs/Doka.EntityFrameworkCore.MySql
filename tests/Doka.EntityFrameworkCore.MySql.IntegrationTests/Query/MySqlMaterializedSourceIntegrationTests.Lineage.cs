namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlMaterializedSourceIntegrationTests
{
    /// <summary>Verifies the named navigation lineage contract against the live database.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task AssertNamedNavigationLineageAsync(
        IntegrationDatabaseTarget target,
        string mappingName,
        CancellationToken cancellationToken
    )
    {
        // Arrange
        await using var fixture = await CreateNamedLineageFixtureAsync(target, cancellationToken);

        // Act
        var query = fixture
            .Context
            .Set<NamedLineageNode>(mappingName)
            .Select(row => row)
            .Where(row => row.Id == 1)
            .WithMaterializedSource(source => source.Select(row => row.Target.Name));

        var actual = await query.SingleAsync(cancellationToken);

        // Assert
        Assert.Equal(
            mappingName == "First"
                ? "first-target"
                : "second-target",
            actual);

        Assert.Equal(1, fixture.Probe.Count);
    }

    /// <summary>Verifies the join result preserves named lineage contract against the live database.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task AssertJoinResultPreservesNamedLineageAsync(
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken
    )
    {
        // Arrange
        await using var fixture = await CreateNamedLineageFixtureAsync(target, cancellationToken);
        var context = fixture.Context;
        var source = context
            .Set<NamedLineageNode>("First")
            .Join(
                context.Set<NamedLineageNode>("Second"),
                first => first.Id,
                second => second.Id,
                (_, second) => second);

        // Act
        var query = source.WithMaterializedSource(bound => bound.Select(row => row.Target.Name));

        var actual = await query.SingleAsync(cancellationToken);

        // Assert
        Assert.Equal("second-target", actual);
        Assert.Equal(1, fixture.Probe.Count);
    }

    /// <summary>Verifies the owned navigation after binding contract against the live database.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task AssertOwnedNavigationAfterBindingAsync(
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken
    )
    {
        // Arrange
        await using var fixture = await CreateMappingFixtureAsync<OwnedMappedItem>(target, cancellationToken);

        // Act
        var query = fixture
            .Context
            .Set<OwnedMappedItem>()
            .WithMaterializedSource(source => source.Select(owner => new
            {
                owner.Id,
                owner.Contact.Detail,
            }))
            .OrderBy(row => row.Id);

        var actual = await query.ToArrayAsync(cancellationToken);

        // Assert
        Assert.Equal([1, 2], actual.Select(row => row.Id));
        Assert.Equal(["detail-1", "detail-2"], actual.Select(row => row.Detail));
        Assert.Equal(1, fixture.Probe.Count);
    }
}
