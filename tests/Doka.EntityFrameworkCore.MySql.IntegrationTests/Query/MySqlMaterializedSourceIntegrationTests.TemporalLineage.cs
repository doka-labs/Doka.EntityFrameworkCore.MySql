namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlMaterializedSourceIntegrationTests
{
    /// <summary>Verifies the temporal navigation after binding contract against the live database.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task AssertTemporalNavigationAfterBindingAsync(
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken
    )
    {
        // Arrange
        await using var fixture = await CreateTemporalLineageFixtureAsync(target, cancellationToken);

        // Act
        var query = fixture
            .Context
            .Parents
            .TemporalAsOf(fixture.Instant)
            .WithMaterializedSource(source => source.Select(parent => new
            {
                parent.Id,
                ChildId = parent.Child.Id,
                ChildName = parent.Child.Name,
            }));

        var actual = await query.SingleAsync(cancellationToken);

        // Assert
        Assert.Equal(1, actual.Id);
        Assert.Equal(1, actual.ChildId);
        Assert.Equal("historical-child", actual.ChildName);
        Assert.Equal(1, fixture.Probe.Count);
    }

    /// <summary>Verifies the projected temporal navigation after binding contract against the live database.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task AssertProjectedTemporalNavigationAfterBindingAsync(
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken
    )
    {
        // Arrange
        await using var fixture = await CreateTemporalLineageFixtureAsync(target, cancellationToken);

        // Act
        var query = fixture
            .Context
            .Parents
            .TemporalAsOf(fixture.Instant)
            .Select(parent => parent.Child)
            .WithMaterializedSource(source => source.Select(child => new
            {
                child.Id,
                NextId = child.Next!.Id,
                NextName = child.Next!.Name,
            }));

        var actual = await query.SingleAsync(cancellationToken);

        // Assert
        Assert.Equal(1, actual.Id);
        Assert.Equal(3, actual.NextId);
        Assert.Equal("historical-next", actual.NextName);
        Assert.Equal(1, fixture.Probe.Count);
    }

    /// <summary>Verifies the matching temporal bindings can concatenate contract against the live database.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task AssertMatchingTemporalBindingsCanConcatenateAsync(
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken
    )
    {
        // Arrange
        await using var fixture = await CreateTemporalLineageFixtureAsync(target, cancellationToken);
        var context = fixture.Context;
        var first = context
            .Parents
            .TemporalAsOf(fixture.Instant)
            .WithMaterializedSource(source => source);

        var second = context
            .Parents
            .TemporalAsOf(fixture.Instant)
            .WithMaterializedSource(source => source);

        // Act
        var query = first.Concat(second);

        var actual = await query.ToArrayAsync(cancellationToken);

        // Assert
        Assert.Equal([1, 1], actual.Select(parent => parent.Id));
        Assert.Equal([1, 1], actual.Select(parent => parent.ChildId));
        Assert.Equal(1, fixture.Probe.Count);
    }

    /// <summary>
    /// Verifies the current and historical bindings reject before io contract against the live database.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task AssertCurrentAndHistoricalBindingsRejectBeforeIoAsync(
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken
    )
    {
        // Arrange
        await using var fixture = await CreateTemporalLineageFixtureAsync(target, cancellationToken);
        var context = fixture.Context;
        var historical = context
            .Parents
            .TemporalAsOf(fixture.Instant)
            .WithMaterializedSource(source => source);

        var current = context.Parents.WithMaterializedSource(source => source);

        // Act
        var query = historical.Concat(current);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(() => query.ToArrayAsync(cancellationToken));

        // Assert
        Assert.Contains("matching temporal operators", exception.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, fixture.Probe.Count);
    }

    /// <summary>Verifies the different temporal bindings reject before io contract against the live database.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task AssertDifferentTemporalBindingsRejectBeforeIoAsync(
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken
    )
    {
        // Arrange
        await using var fixture = await CreateTemporalLineageFixtureAsync(target, cancellationToken);
        var context = fixture.Context;
        var first = context
            .Parents
            .TemporalAsOf(fixture.Instant)
            .WithMaterializedSource(source => source);

        var second = context
            .Parents
            .TemporalAsOf(fixture.Instant.AddDays(1))
            .WithMaterializedSource(source => source);

        // Act
        var query = first.Concat(second);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(() => query.ToArrayAsync(cancellationToken));

        // Assert
        Assert.Contains("identical UTC boundaries", exception.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, fixture.Probe.Count);
    }
}
