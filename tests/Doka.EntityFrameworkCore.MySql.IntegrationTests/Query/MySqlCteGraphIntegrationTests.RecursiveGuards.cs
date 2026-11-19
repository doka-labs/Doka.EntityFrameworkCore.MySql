namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlCteGraphIntegrationTests
{
    /// <summary>Reports each structurally unsupported recursive operator without conflating their failure oracles.</summary>
    public static TheoryData<string, string, string> UnsupportedStepCases
    {
        get
        {
            var cases = new TheoryData<string, string, string>();

            foreach (var target in SelectedTargets)
            {
                cases.Add(target, "multiple", "exactly one frontier");
                cases.Add(target, "aggregate", "grouping");
                cases.Add(target, "union", "set operations");
                cases.Add(target, "nested", "another statement-local");
                cases.Add(target, "outer", "outer-joined");
                cases.Add(target, "paging", "paging");
            }

            return cases;
        }
    }

    /// <summary>CTE-N03/N08/N10 reject unsupported recursive structure before a data command.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(UnsupportedStepCases))]
    public async Task Unsupported_recursive_operator_is_rejected_with_its_specific_boundary(
        string target,
        string operation,
        string expectedReason
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        var commands = new GraphCommands();
        await using var context = store.CreateContext(commands);
        var query = context
            .Vertices
            .Select(row => row.Id)
            .WithRecursiveCte(
                BuildUnsupportedStep(context, operation),
                rows => rows,
                2);

        // Act
        var failure = await Record.ExceptionAsync(() => query.ToArrayAsync(safety.Token));

        // Assert
        Assert.Contains(expectedReason,
            Assert
                .IsType<InvalidOperationException>(failure)
                .ToString(),
            StringComparison.OrdinalIgnoreCase);

        Assert.Empty(commands.Reads);
        Assert.Empty(commands.Writes);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Uses actual LINQ expression forms rather than client-only helper calls as rejection substitutes.</summary>
    private static Expression<Func<IQueryable<int>, IQueryable<int>>> BuildUnsupportedStep(
        GraphContext context,
        string operation
    ) => operation switch
    {
        "multiple" => frontier => frontier.Join(frontier, left => left, right => right, (left, right) => left + right),
        "aggregate" => frontier => frontier
            .GroupBy(_ => 1)
            .Select(group => group.Count()),
        "union" => frontier => frontier.Union(frontier),
        "nested" => frontier => frontier.WithCte(rows => rows),
        "paging" => frontier => frontier.Take(1),
        "outer" => frontier => frontier
            .GroupJoin(context.Edges,
                id => id,
                edge => edge.ParentId,
                (id, edges) => new
                {
                    Id = id,
                    Edges = edges,
                })
            .SelectMany(pair => pair.Edges.DefaultIfEmpty(),
                (pair, edge) => edge == null
                    ? pair.Id
                    : edge.ChildId),
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };
}
