namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlMaterializedSourceIntegrationTests
{
    private static async Task AssertTemporalNavigationAfterBindingAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using var fixture = await CreateTemporalLineageFixtureAsync(target);
        var query = fixture.Context.Parents.TemporalAsOf(fixture.Instant)
            .WithMaterializedSource(source => source.Select(parent => new
            {
                parent.Id,
                ChildId = parent.Child.Id,
                ChildName = parent.Child.Name,
            }));

        var actual = await query.SingleAsync(CancellationToken.None);

        Assert.Equal(1, actual.Id);
        Assert.Equal(1, actual.ChildId);
        Assert.Equal("historical-child", actual.ChildName);
        Assert.Equal(1, fixture.Probe.Count);
    }

    private static async Task AssertProjectedTemporalNavigationAfterBindingAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using var fixture = await CreateTemporalLineageFixtureAsync(target);
        var query = fixture.Context.Parents.TemporalAsOf(fixture.Instant)
            .Select(parent => parent.Child)
            .WithMaterializedSource(source => source.Select(child => new
            {
                child.Id,
                NextId = child.Next!.Id,
                NextName = child.Next!.Name,
            }));

        var actual = await query.SingleAsync(CancellationToken.None);

        Assert.Equal(1, actual.Id);
        Assert.Equal(3, actual.NextId);
        Assert.Equal("historical-next", actual.NextName);
        Assert.Equal(1, fixture.Probe.Count);
    }

    private static async Task AssertMatchingTemporalBindingsCanConcatenateAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using var fixture = await CreateTemporalLineageFixtureAsync(target);
        var context = fixture.Context;
        var first = context.Parents.TemporalAsOf(fixture.Instant).WithMaterializedSource(source => source);
        var second = context.Parents.TemporalAsOf(fixture.Instant).WithMaterializedSource(source => source);
        var query = first.Concat(second);

        var actual = await query.ToArrayAsync(CancellationToken.None);

        Assert.Equal([1, 1], actual.Select(parent => parent.Id));
        Assert.Equal([1, 1], actual.Select(parent => parent.ChildId));
        Assert.Equal(1, fixture.Probe.Count);
    }

    private static async Task AssertCurrentAndHistoricalBindingsRejectBeforeIoAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using var fixture = await CreateTemporalLineageFixtureAsync(target);
        var context = fixture.Context;
        var historical = context.Parents.TemporalAsOf(fixture.Instant).WithMaterializedSource(source => source);
        var current = context.Parents.WithMaterializedSource(source => source);
        var query = historical.Concat(current);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => query.ToArrayAsync(CancellationToken.None));

        Assert.Contains("matching temporal operators", exception.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, fixture.Probe.Count);
    }

    private static async Task AssertDifferentTemporalBindingsRejectBeforeIoAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using var fixture = await CreateTemporalLineageFixtureAsync(target);
        var context = fixture.Context;
        var first = context.Parents.TemporalAsOf(fixture.Instant).WithMaterializedSource(source => source);
        var second = context.Parents.TemporalAsOf(fixture.Instant.AddDays(1))
            .WithMaterializedSource(source => source);

        var query = first.Concat(second);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => query.ToArrayAsync(CancellationToken.None));

        Assert.Contains("identical UTC boundaries", exception.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, fixture.Probe.Count);
    }
}
