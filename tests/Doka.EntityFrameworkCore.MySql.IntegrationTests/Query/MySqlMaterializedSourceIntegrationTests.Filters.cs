namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlMaterializedSourceIntegrationTests
{
    private static async Task AssertDistinctGlobalFilterParametersAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using var fixture = await CreateFilterFixtureAsync(target);
        var query = CreateFilteredTripleQuery(fixture.Context);

        var actual = await query.ToArrayAsync(CancellationToken.None);

        AssertFilteredTriples(actual, 2, 4, 6);
        Assert.Equal(3, fixture.Probe.ParameterValues.Length);
        Assert.Contains(2, fixture.Probe.ParameterValues);
        Assert.Contains(4, fixture.Probe.ParameterValues);
        Assert.Contains(6, fixture.Probe.ParameterValues);
        Assert.Equal(1, fixture.Probe.Count);
    }

    private static async Task AssertCachedGlobalFiltersUseCurrentContextValuesAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using var fixture = await CreateFilterFixtureAsync(target);
        var query = CreateFilteredTripleQuery(fixture.Context);
        var previous = await query.ToArrayAsync(CancellationToken.None);
        fixture.Context.MinimumAValue = 3;
        fixture.Context.MinimumBValue = 5;
        fixture.Probe.Reset();

        var actual = await query.ToArrayAsync(CancellationToken.None);

        AssertFilteredTriples(previous, 2, 4, 6);
        AssertFilteredTriples(actual, 3, 5, 6);
        Assert.Equal(3, fixture.Probe.ParameterValues.Length);
        Assert.Contains(3, fixture.Probe.ParameterValues);
        Assert.Contains(5, fixture.Probe.ParameterValues);
        Assert.Contains(6, fixture.Probe.ParameterValues);
        Assert.Equal(1, fixture.Probe.Count);
    }

    private static IQueryable<FilterTriple> CreateFilteredTripleQuery(
        FilterContext context
    ) => context.SourceA.WithMaterializedSource(sourceA => context.SourceB.WithMaterializedSource(sourceB => sourceA
            .SelectMany(_ => sourceB, (left, right) => new { AId = left.Id, BId = right.Id })
            .SelectMany(_ => context.SourceC, (pair, third) => new FilterTriple
            {
                AId = pair.AId,
                BId = pair.BId,
                CId = third.Id,
            })))
        .OrderBy(row => row.AId)
        .ThenBy(row => row.BId)
        .ThenBy(row => row.CId);

    private static void AssertFilteredTriples(
        FilterTriple[] actual,
        int minimumA,
        int minimumB,
        int minimumC
    )
    {
        var expected = Enumerable.Range(minimumA, 7 - minimumA)
            .SelectMany(_ => Enumerable.Range(minimumB, 7 - minimumB), (first, second) => new { first, second })
            .SelectMany(
                _ => Enumerable.Range(minimumC, 7 - minimumC),
                (pair, third) => (pair.first, pair.second, third))
            .ToArray();

        Assert.Equal(expected, actual.Select(row => (row.AId, row.BId, row.CId)));
    }

    private static async Task AssertPagedGlobalFilterAppliedOnlyToSourceAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using var fixture = await CreateFilterFixtureAsync(target);
        var query = fixture.Context.SourceA.OrderBy(row => row.Id)
            .Skip(1)
            .Take(2)
            .WithMaterializedSource(source => source.Join(
                source,
                left => left.Id,
                right => right.Id,
                (left, _) => left))
            .OrderBy(row => row.Id);

        var actual = await query.ToArrayAsync(CancellationToken.None);

        Assert.Equal([3, 4], actual.Select(row => row.Id));
        // WHY: Reapplying this deterministic filter would keep the same rows; the
        // executed SQL must also prove it appears only once before source paging.
        Assert.Equal(1, fixture.Probe.CommandText.Split(">=", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, fixture.Probe.Count);
    }

    private static async Task AssertIgnoredFiltersKeepQueryWideSemanticsAsync(
        IntegrationDatabaseTarget target,
        bool ignoreOnSource
    )
    {
        await using var fixture = await CreateFilterFixtureAsync(target);
        IQueryable<FilterSourceA> source = fixture.Context.SourceA;

        if (ignoreOnSource)
        {
            source = source.IgnoreQueryFilters();
        }

        var context = fixture.Context;
        var query = source.OrderBy(row => row.Id)
            .Skip(1)
            .Take(2)
            .WithMaterializedSource(bound => bound.SelectMany(_ => context.SourceC, (left, right) => new FilterTriple
            {
                AId = left.Id,
                CId = right.Id,
            }));

        if (!ignoreOnSource)
        {
            query = query.IgnoreQueryFilters();
        }

        var ordered = query.OrderBy(row => row.AId).ThenBy(row => row.CId);

        var actual = await ordered.ToArrayAsync(CancellationToken.None);

        var expected = Enumerable.Range(2, 2)
            .SelectMany(_ => Enumerable.Range(1, 6), (first, third) => (first, third));

        Assert.Equal(expected, actual.Select(row => (row.AId, row.CId)));
        Assert.DoesNotContain(">=", fixture.Probe.CommandText, StringComparison.Ordinal);
        Assert.Equal(1, fixture.Probe.Count);
    }

    private sealed class FilterTriple
    {
        public int AId { get; set; }

        public int BId { get; set; }

        public int CId { get; set; }
    }
}
