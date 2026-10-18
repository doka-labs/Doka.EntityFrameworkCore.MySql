namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlMaterializedSourceIntegrationTests
{
    private static async Task AssertChangedParametersAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using var fixture = await CreateFixtureAsync(target);
        var minimum = 31;
        var query = fixture.Context.Items.Where(item => item.Id >= minimum)
            .WithMaterializedSource(source => source.Select(item => item.Id))
            .OrderBy(id => id);

        var previous = await query.ToArrayAsync(CancellationToken.None);
        minimum = 32;
        fixture.Probe.Reset();

        var actual = await query.ToArrayAsync(CancellationToken.None);

        Assert.Equal([31, 32], previous);
        Assert.Equal([32], actual);
        Assert.Contains(32, fixture.Probe.ParameterValues);
        Assert.Equal(1, fixture.Probe.Count);
    }

    private static async Task AssertRepeatedEnumerationAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using var fixture = await CreateFixtureAsync(target);
        var query = fixture.Context.Items.Where(item => item.Id == 1)
            .Select(_ => Guid.NewGuid())
            .WithMaterializedSource(source => source);
        var previous = await query.SingleAsync(CancellationToken.None);
        fixture.Probe.Reset();

        var actual = await query.SingleAsync(CancellationToken.None);

        Assert.NotEqual(Guid.Empty, actual);
        Assert.NotEqual(previous, actual);
        Assert.Equal(1, fixture.Probe.Count);
    }

    private static async Task AssertCompiledQueryAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using var fixture = await CreateFixtureAsync(target);
        var query = EF.CompileAsyncQuery<BindingContext, int, int>((context, minimum) => context.Items
            .Where(item => item.Id >= minimum)
            .WithMaterializedSource(source => source.Select(item => item.Id))
            .OrderBy(id => id));

        var actual = await ReadAsync(query(fixture.Context, 31));

        Assert.Equal([31, 32], actual);
        Assert.Equal(1, fixture.Probe.Count);
    }

    private static async Task<int[]> ReadAsync(
        IAsyncEnumerable<int> source
    )
    {
        var result = new List<int>();

        await foreach (var value in source.WithCancellation(CancellationToken.None))
        {
            result.Add(value);
        }

        return result.ToArray();
    }

    private static async Task AssertEntityTrackingAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using var fixture = await CreateFixtureAsync(target);
        var query = fixture.Context.Items.Where(item => item.Id <= 2)
            .WithMaterializedSource(source => source.Join(
                source,
                left => left.Id,
                right => right.Id,
                (left, _) => left))
            .OrderBy(item => item.Id);

        var actual = await query.ToArrayAsync(CancellationToken.None);

        Assert.Equal([1, 2], actual.Select(item => item.Id));
        Assert.Equal(2, fixture.Context.ChangeTracker.Entries<BoundItem>().Count());
        Assert.All(actual, item => Assert.Equal(EntityState.Unchanged, fixture.Context.Entry(item).State));
        Assert.Equal(1, fixture.Probe.Count);
    }

    private static async Task AssertCollectionResultsAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using var fixture = await CreateFixtureAsync(target);
        var query = fixture.Context.Items.Where(item => item.Id <= 3)
            .WithMaterializedSource(source => source.Select(item => new CollectionResult
            {
                Id = item.Id,
                Children = item.Children.OrderBy(child => child.Id).Select(child => child.Name).ToArray(),
            }))
            .OrderBy(item => item.Id);

        var actual = await query.ToArrayAsync(CancellationToken.None);

        Assert.Equal([1, 2, 3], actual.Select(item => item.Id));
        Assert.Equal(["child-1", "child-1b"], actual[0].Children);
        Assert.Equal(["child-2"], actual[1].Children);
        Assert.Empty(actual[2].Children);
        Assert.Equal(1, fixture.Probe.Count);
    }

    private static async Task AssertCancellationAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using var fixture = await CreateFixtureAsync(target);
        var interceptor = new CommandCancellationProbeInterceptor();
        await using var context = CreateContext(target, interceptor);
        using var cancellation = new CancellationTokenSource();
        var query = context.Items.WithMaterializedSource(source => source.Where(item => item.Id <= 2));

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => query.ToArrayAsync(cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(cancellation.Token, interceptor.ReceivedCancellationToken);
        Assert.Equal(1, interceptor.InvocationCount);
    }

    private static async Task AssertConstructionAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using var fixture = await CreateFixtureAsync(target);

        var query = fixture.Context.Items.Where(item => item.Id <= 2)
            .WithMaterializedSource(source => source.Join(
                source,
                left => left.Id,
                right => right.Id,
                (left, _) => left))
            .ToQueryString();

        Assert.Contains("WITH RECURSIVE", query, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, fixture.Probe.Count);
        Assert.Empty(fixture.Context.ChangeTracker.Entries());
    }

    private static async Task AssertClientProjectionRejectedAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using var fixture = await CreateFixtureAsync(target);
        var query = fixture.Context.Items.Select(item => ClientOnlyProjection(item.Name))
            .WithMaterializedSource(source => source);

        await Assert.ThrowsAsync<InvalidOperationException>(() => query.ToArrayAsync(CancellationToken.None));

        Assert.Equal(0, fixture.Probe.Count);
    }

    private static string ClientOnlyProjection(
        string value
    ) => $"client:{value}";

    private static async Task AssertCollectionSourceRejectedAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using var fixture = await CreateFixtureAsync(target);
        var query = fixture.Context.Items.Select(item => new CollectionResult
            {
                Id = item.Id,
                Children = item.Children.Select(child => child.Name).ToArray(),
            })
            .WithMaterializedSource(source => source.Select(item => item.Id));

        await Assert.ThrowsAsync<InvalidOperationException>(() => query.ToArrayAsync(CancellationToken.None));

        Assert.Equal(0, fixture.Probe.Count);
    }

    private static async Task AssertForeignContextRejectedAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using var fixture = await CreateFixtureAsync(target);
        var foreignProbe = new BindingCommandProbe();
        await using var foreign = CreateContext(target, foreignProbe);
        var query = fixture.Context.Items.WithMaterializedSource(source => source.Join(
            foreign.Items,
            local => local.Id,
            remote => remote.Id,
            (local, _) => local.Id));

        await Assert.ThrowsAsync<InvalidOperationException>(() => query.ToArrayAsync(CancellationToken.None));

        Assert.Equal(0, fixture.Probe.Count);
        Assert.Equal(0, foreignProbe.Count);
    }
}
