namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlCteGraphIntegrationTests
{
    /// <summary>Separates normal filter behavior from the consumer's explicit global opt-out.</summary>
    public static TheoryData<string, bool> FilterCases
    {
        get
        {
            var cases = new TheoryData<string, bool>();

            foreach (var target in SelectedTargets)
            {
                cases.Add(target, false);
                cases.Add(target, true);
            }

            return cases;
        }
    }

    /// <summary>CTE-P12/P13/N14 retain source and independently filtered navigation scopes with explicit opt-out.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(FilterCases))]
    public async Task Native_source_and_navigated_target_filters_keep_their_normal_scope(
        string target,
        bool ignoreFilters
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        var commands = new GraphCommands();
        await using var context = store.CreateContext(commands);
        var query = context
            .Vertices
            .Where(row => row.Id <= 4)
            .WithCte(rows => rows
                .OrderBy(row => row.Id)
                .Select(row => new
                {
                    row.Id,
                    Target = row.Target.Name,
                }));

        if (ignoreFilters)
        {
            query = query.IgnoreQueryFilters();
        }

        int[] expectedIds = ignoreFilters
            ? [1, 2, 3, 4]
            : [1, 4];

        string[] expectedTargets = ignoreFilters
            ? ["visible", "hidden", "visible", "visible"]
            : ["visible", "visible"];

        // Act
        var actual = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal(expectedIds, actual.Select(row => row.Id));
        Assert.Equal(expectedTargets, actual.Select(row => row.Target));
        Assert.Single(commands.Reads);
    }

    /// <summary>CTE-P12 retains the separate outer root filter instead of applying only the bound source's filter.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Separate_outer_entity_roots_keep_their_tenant_filter_after_binding(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        await store.ExecuteAsync("UPDATE `GraphEdges` SET `TenantId`=2 WHERE `Id`=2;", safety.Token);
        await using var context = store.CreateContext();

        // Act
        var actual = await context
            .Vertices
            .Select(row => row.Id)
            .WithCte(ids => context
                .Edges
                .Where(edge => ids.Contains(edge.ParentId))
                .OrderBy(edge => edge.Id)
                .Select(edge => edge.Id))
            .ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([1, 3], actual);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>CTE-P10/P23 rehydrate physical entities before reference Include, preserving both filters and tracking.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Scalar_cte_rejoin_reference_include_returns_the_correct_filtered_entities(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        var commands = new GraphCommands();
        await using var context = store.CreateContext(commands);

        // Act
        var actual = await context
            .Vertices
            .Where(row => row.Id <= 4)
            .Select(row => row.Id)
            .WithCte(ids => context
                .Vertices
                .Where(row => ids.Contains(row.Id))
                .Include(row => row.Target)
                .OrderBy(row => row.Id))
            .ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([1, 4], actual.Select(row => row.Id));
        Assert.All(actual, row => Assert.Equal("visible", row.Target.Name));
        Assert.Single(actual
            .Select(row => row.Target)
            .Distinct(ReferenceEqualityComparer.Instance));

        Assert.Equal(3,
            context
                .ChangeTracker
                .Entries()
                .Count());

        Assert.Single(commands.Reads);
    }
}
