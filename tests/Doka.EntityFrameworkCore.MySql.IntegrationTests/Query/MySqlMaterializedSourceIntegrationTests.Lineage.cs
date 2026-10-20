namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlMaterializedSourceIntegrationTests
{
    private static async Task AssertNamedNavigationLineageAsync(
        IntegrationDatabaseTarget target,
        string mappingName
    )
    {
        await using var fixture = await CreateNamedLineageFixtureAsync(target);
        var query = fixture.Context.Set<NamedLineageNode>(mappingName)
            .Select(row => row)
            .Where(row => row.Id == 1)
            .WithMaterializedSource(source => source.Select(row => row.Target.Name));

        var actual = await query.SingleAsync(CancellationToken.None);

        Assert.Equal(mappingName == "First" ? "first-target" : "second-target", actual);
        Assert.Equal(1, fixture.Probe.Count);
    }

    private static async Task AssertJoinResultPreservesNamedLineageAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using var fixture = await CreateNamedLineageFixtureAsync(target);
        var context = fixture.Context;
        var source = context.Set<NamedLineageNode>("First").Join(
            context.Set<NamedLineageNode>("Second"),
            first => first.Id,
            second => second.Id,
            (_, second) => second);

        var query = source.WithMaterializedSource(bound => bound.Select(row => row.Target.Name));

        var actual = await query.SingleAsync(CancellationToken.None);

        Assert.Equal("second-target", actual);
        Assert.Equal(1, fixture.Probe.Count);
    }

    private static async Task AssertOwnedNavigationAfterBindingAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using var fixture = await CreateMappingFixtureAsync<OwnedMappedItem>(target);
        var query = fixture.Context.Set<OwnedMappedItem>()
            .WithMaterializedSource(source => source.Select(owner => new
            {
                owner.Id,
                owner.Contact.Detail,
            }))
            .OrderBy(row => row.Id);

        var actual = await query.ToArrayAsync(CancellationToken.None);

        Assert.Equal([1, 2], actual.Select(row => row.Id));
        Assert.Equal(["detail-1", "detail-2"], actual.Select(row => row.Detail));
        Assert.Equal(1, fixture.Probe.Count);
    }
}
