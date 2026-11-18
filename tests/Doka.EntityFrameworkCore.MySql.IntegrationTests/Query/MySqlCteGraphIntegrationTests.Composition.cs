namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlCteGraphIntegrationTests
{
    /// <summary>Separates deterministic derived-key ordering from volatile native ordering after duplicate removal.</summary>
    public static IEnumerable<TheoryDataRow<string, bool>> RecursiveDistinctOrderCases
        => SelectedTargets.SelectMany(target
            => new[] { new TheoryDataRow<string, bool>(target.Data, false), new(target.Data, true) });

    /// <summary>Separates native UNION duplicate removal from UNION ALL's retained occurrences.</summary>
    public static IEnumerable<TheoryDataRow<string, bool>> SourceSetCases => SelectedTargets.SelectMany(target => new[]
    {
        new TheoryDataRow<string, bool>(target.Data, false), new(target.Data, true),
    });

    /// <summary>CTE-P06/P07 keep DISTINCT equivalence classes intact when the outer sort key is not itself projected.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(RecursiveDistinctOrderCases))]
    public async Task Recursive_distinct_outer_order_preserves_exact_occurrences_for_derived_and_volatile_keys(
        string target,
        bool volatileOrder
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        var commands = new GraphCommands();
        await using var context = store.CreateContext(commands);
        var source = context
            .Vertices
            .Where(row => row.Id == 1)
            .Select(row => row.Id);

        var query = volatileOrder
            ? source.WithRecursiveCte(frontier => frontier.Select(id => id + 1),
                rows => rows
                    .Concat(rows)
                    .Distinct()
                    .OrderBy(_ => Guid.NewGuid()),
                3)
            : source.WithRecursiveCte(frontier => frontier.Select(id => id + 1),
                rows => rows
                    .Concat(rows)
                    .Distinct()
                    .OrderBy(id => id + 10),
                3);

        // Act
        var actual = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([1, 2, 3, 4], actual.Order());

        if (!volatileOrder)
        {
            Assert.Equal([1, 2, 3, 4], actual);
        }

        Assert.Single(commands.Reads);
        Assert.Empty(commands.Writes);
    }

    /// <summary>CTE-P09 preserves native set-source occurrences before the completed relation is bound.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SourceSetCases))]
    public async Task Completed_set_source_keeps_its_native_duplicate_semantics(
        string target,
        bool distinct
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        var commands = new GraphCommands();
        await using var context = store.CreateContext(commands);
        var source = context.Facets.Select(row => row.Id);
        var extra = context
            .Vertices
            .Where(row => row.Id == 1)
            .Select(row => row.Id);

        var complete = distinct
            ? source.Union(extra)
            : source.Concat(extra);

        var query = complete.WithCte(rows => rows.OrderBy(id => id));
        int[] expected = distinct
            ? [1, 2]
            : [1, 1, 2];

        // Act
        var actual = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal(expected, actual);
        var sql = Assert.Single(commands.Reads)
            .Sql;

        Assert.Contains(distinct
                ? "UNION\n"
                : "UNION ALL",
            sql,
            StringComparison.Ordinal);

        Assert.Equal(1, CountOrdinaryDefinitions(sql));
    }

    /// <summary>CTE-P01/P05 distinguish captured values in two equal-shaped independent definitions.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Separate_equal_shaped_bindings_keep_distinct_native_parameters_and_results(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        var commands = new GraphCommands();
        await using var context = store.CreateContext(commands);
        var firstMaximum = 2;
        var secondMaximum = 4;

        // Act
        var actual = await context
            .Vertices
            .Where(row => row.Id <= firstMaximum)
            .Select(row => row.Id)
            .WithCte(first =>
                context
                    .Vertices
                    .Where(row => row.Id <= secondMaximum)
                    .Select(row => row.Id)
                    .WithCte(second =>
                        first.Join(second, left => left, right => right, (left, right) => left + right)))
            .OrderBy(value => value)
            .ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([2, 4], actual);
        var sql = Assert.Single(commands.Reads)
            .Sql;

        Assert.Equal(2, CountOrdinaryDefinitions(sql));
        Assert.Contains("@", sql, StringComparison.Ordinal);
    }

    /// <summary>CTE-P02 consumes both independent definitions and preserves their actual cross-join multiplicity.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Independent_definitions_are_both_consumed_by_the_native_cross_join(
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
            .Where(row => row.Id <= 2)
            .Select(row => row.Id)
            .WithCte(first =>
                context
                    .Edges
                    .Where(row => row.Id <= 2)
                    .Select(row => row.Id)
                    .WithCte(second => first.SelectMany(
                        _ => second,
                        (left, right) => new
                        {
                            Left = left,
                            Right = right,
                        })))
            .OrderBy(row => row.Left)
            .ThenBy(row => row.Right)
            .ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([(1, 1), (1, 2), (2, 1), (2, 2)], actual.Select(row => (row.Left, row.Right)));
        Assert.Equal(2,
            CountOrdinaryDefinitions(Assert.Single(commands.Reads)
                .Sql));
    }

    /// <summary>CTE-P03 consumes both arms of a real typed dependency diamond without duplicating its common definition.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Typed_dependency_diamond_has_three_definitions_and_exact_native_values(
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
            .Select(row => row.Id)
            .WithCte(common => common
                .Where(id => id <= 4)
                .WithCte(left =>
                    common
                        .Where(id => id >= 2)
                        .WithCte(right => left.Join(right, id => id, id => id, (first, second) => first + second))))
            .OrderBy(value => value)
            .ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([4, 8], actual);
        var sql = Assert.Single(commands.Reads)
            .Sql;

        Assert.Equal(3, CountOrdinaryDefinitions(sql));
        Assert.Equal(1, Regex.Count(sql, @"FROM `GraphVertices`"));
    }

    /// <summary>CTE-P06 explicitly separates duplicate join occurrences from an optional outer DISTINCT.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(FilterCases))]
    public async Task Duplicate_sources_multiply_in_a_join_until_outer_distinct_is_requested(
        string target,
        bool distinct
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        var one = context
            .Vertices
            .Where(row => row.Id == 1)
            .Select(row => row.Id);

        var query = one
            .Concat(one)
            .WithCte(rows => rows.Join(rows, id => id, id => id, (left, right) => left + right));

        if (distinct)
        {
            query = query.Distinct();
        }

        // Act
        var actual = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal(distinct
                ? 1
                : 4,
            actual.Length);

        Assert.All(actual, value => Assert.Equal(2, value));
    }

    /// <summary>CTE-P07 retains the source page independently of a later ordered outer page.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Source_and_outer_pages_are_applied_at_their_distinct_relational_scopes(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();

        // Act
        var actual = await context
            .Vertices
            .OrderBy(row => row.Id)
            .Skip(1)
            .Take(3)
            .Select(row => row.Id)
            .WithCte(rows => rows.Concat(rows))
            .OrderBy(id => id)
            .Skip(2)
            .Take(3)
            .ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([4, 4, 5], actual);
    }

    /// <summary>CTE-P09 executes a real grouped source and filters its aggregate values in the outer scope.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Flat_aggregate_relation_preserves_native_group_counts(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();

        // Act
        var actual = await context
            .Vertices
            .GroupBy(row => row.TargetId)
            .Select(group => new
            {
                TargetId = group.Key,
                Count = group.Count(),
            })
            .WithCte(rows => rows.Where(row => row.Count > 1))
            .ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([(10, 4)], actual.Select(row => (row.TargetId, row.Count)));
    }

    /// <summary>CTE-P09 preserves nullable aggregate empty-input semantics through a bound relation.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Nullable_aggregate_relation_preserves_the_existing_ef_sum_contract(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();

        // Act
        var actual = await context
            .Facets
            .Where(row => row.Id == 2)
            .GroupBy(row => row.State)
            .Select(group => new
            {
                group.Key,
                Sum = group.Sum(row => row.Amount),
            })
            .WithCte(rows => rows)
            .SingleAsync(safety.Token);

        // Assert
        Assert.Equal(GraphState.Done, actual.Key);
        Assert.Equal(0m, actual.Sum);
    }

    /// <summary>CTE-P28 records actual compilation/materialization allocations and data-command count without a timing gate.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Deep_typed_definition_chain_has_linear_sql_and_one_complete_native_result(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        var commands = new GraphCommands();
        await using var context = store.CreateContext(commands);
        var query = context
            .Vertices
            .Where(row => row.Id <= 2)
            .Select(row => row.Id);

        // WHY: This constructs a fixed deep expression; the query itself is still one deferred act.
        for (var depth = 0; depth < 12; depth++)
        {
            query = query.WithCte(rows => rows.Select(id => id + 1));
        }

        var before = GC.GetTotalAllocatedBytes(precise: true);

        // Act
        var actual = await query
            .OrderBy(id => id)
            .ToArrayAsync(safety.Token);

        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        // Assert
        Assert.Equal([13, 14], actual);
        Assert.Equal(12,
            CountOrdinaryDefinitions(Assert.Single(commands.Reads)
                .Sql));

        // WHY: Process-wide bytes include concurrent test work. Record diagnostics, never
        // turn hardware/load variation or runtime allocation-counter noise into a pass gate.
        _output.WriteLine(
            "CTE deep-chain diagnostic: allocated-process-bytes={0}; data-commands=1.",
            allocated);
    }

    /// <summary>Counts declaration headers, excluding the separate reference aliases in consuming SELECTs.</summary>
    private static int CountOrdinaryDefinitions(
        string sql
    ) => Regex.Count(sql, @"`__doka_cte_\d+(?:_\d+)?` \(");
}
