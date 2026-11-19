namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlCteGraphIntegrationTests
{
    private static readonly (string Topology, int Depth, int[] Expected)[] s_graphCases =
    [
        ("chain", 3, [1, 2, 3, 4]),
        ("branch", 2, [1, 2, 3, 4, 5, 6]),
        ("branch", 0, [1]),
        ("branch", 1, [1, 2, 3]),
        ("diamond", 2, [1, 2, 3, 4, 4]),
        ("self-cycle", 3, [1, 1, 1, 1]),
        ("long-cycle", 5, [1, 1, 2, 2, 3, 3]),
        ("empty", 3, [1]),
    ];

    /// <summary>Reports actual chain, branching, repeated-path, cycle and immediately-empty-step bags separately.</summary>
    public static TheoryData<string, string, int, int[]> GraphCases
    {
        get
        {
            var cases = new TheoryData<string, string, int, int[]>();

            foreach (var target in SelectedTargets)
            {
                foreach (var shape in s_graphCases)
                {
                    cases.Add(target, shape.Topology, shape.Depth, shape.Expected);
                }
            }

            return cases;
        }
    }

    /// <summary>CTE-P16-P20 traverse real stored edges, retaining every UNION ALL occurrence through the selected depth.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(GraphCases))]
    public async Task Stored_graph_traversal_preserves_the_exact_bounded_occurrence_bag(
        string target,
        string topology,
        int maximumDepth,
        int[] expected
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        await store.SetEdgesAsync(topology, safety.Token);
        var commands = new GraphCommands();
        await using var context = store.CreateContext(commands);
        var query = context
            .Vertices
            .Where(vertex => vertex.Id == 1)
            .Select(vertex => vertex.Id)
            .WithRecursiveCte(
                frontier => frontier.Join(context.Edges, id => id, edge => edge.ParentId, (_, edge) => edge.ChildId),
                rows => rows.OrderBy(id => id),
                maximumDepth);

        // Act
        var actual = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal(expected, actual);
        var command = Assert.Single(commands.Reads);
        Assert.Equal(safety.Token, command.Token);
        Assert.Contains("WITH RECURSIVE", command.Sql, StringComparison.Ordinal);
        Assert.Contains("UNION ALL", command.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SET SESSION", command.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>CTE-P19 gives outer DISTINCT its explicit semantics without changing recursive path occurrences.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Explicit_outer_distinct_collapses_the_repeated_diamond_result_only_after_recursion(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        await store.SetEdgesAsync("diamond", safety.Token);
        var commands = new GraphCommands();
        await using var context = store.CreateContext(commands);

        // Act
        var actual = await context
            .Vertices
            .Where(vertex => vertex.Id == 1)
            .Select(vertex => vertex.Id)
            .WithRecursiveCte(
                frontier => frontier.Join(context.Edges, id => id, edge => edge.ParentId, (_, edge) => edge.ChildId),
                rows => rows
                    .Distinct()
                    .OrderBy(id => id),
                2)
            .ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([1, 2, 3, 4], actual);
        var command = Assert.Single(commands.Reads);
        Assert.Contains("UNION ALL", command.Sql, StringComparison.Ordinal);
        Assert.Contains("SELECT DISTINCT", command.Sql, StringComparison.Ordinal);
    }

    /// <summary>CTE-P20 distinguishes a truly empty anchor from the nonempty anchor with an empty step.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Empty_stored_anchor_remains_empty_without_an_outer_take_zero(
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
            .Where(vertex => vertex.Id == -1)
            .Select(vertex => vertex.Id)
            .WithRecursiveCte(
                frontier => frontier.Join(context.Edges, id => id, edge => edge.ParentId, (_, edge) => edge.ChildId),
                rows => rows,
                3)
            .ToArrayAsync(safety.Token);

        // Assert
        Assert.Empty(actual);
        Assert.Single(commands.Reads);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }
}
