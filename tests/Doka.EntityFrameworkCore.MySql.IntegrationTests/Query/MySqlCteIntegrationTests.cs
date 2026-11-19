namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Checks native CTE results independently of translation-only SQL assertions.</summary>
[IntegrationDatabaseTest]
[Trait("VerificationLane", "FullIntegration")]
public sealed partial class MySqlCteIntegrationTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Uses the current test's runner-owned diagnostic output.</summary>
    /// <param name="output">The output helper associated with this test case.</param>
    public MySqlCteIntegrationTests(
        ITestOutputHelper output
    )
    {
        _output = output;
    }

    /// <summary>Runs each supported pinned engine as a separately reported case.</summary>
    public static TheoryData<string> SelectedTargets => new(IntegrationTestEnvironment
        .GetSelectedTargets()
        .Where(target => IntegrationTestEnvironment
            .GetSupportedTargets()
            .Contains(target))
        .Select(target => target.ToString()));

    /// <summary>Separates anchor-only recursion from one and several recursive iterations.</summary>
    public static TheoryData<string, int> DepthCases
    {
        get
        {
            var cases = new TheoryData<string, int>();

            foreach (var target in SelectedTargets)
            {
                cases.Add(target, 0);
                cases.Add(target, 1);
                cases.Add(target, 4);
            }

            return cases;
        }
    }

    /// <summary>A shared ordinary relation preserves duplicate multiplicity and the completed source page.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Ordinary_bag_references_preserve_the_source_page(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await CteStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        var query = context
            .Rows
            .OrderBy(row => row.Id)
            .Skip(1)
            .Take(2)
            .Select(row => row.Id)
            .WithCte(rows => rows.Concat(rows))
            .OrderBy(id => id);

        // Act
        var values = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([2, 2, 3, 3], values);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.DoesNotContain("WHERE 1 = 0", query.ToQueryString(), StringComparison.Ordinal);
    }

    /// <summary>Each visible scalar survives hidden recursion controls and actual checked materialization.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(DepthCases))]
    public async Task Recursive_scalar_results_include_exactly_the_requested_depth(
        string target,
        int depth
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await CteStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        var query = context
            .Rows
            .Where(row => row.Id == 1)
            .Select(row => row.Id)
            .WithRecursiveCte(frontier => frontier.Select(value => value + 1),
                rows => rows.OrderBy(value => value),
                depth);

        // Act
        var values = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal(Enumerable.Range(1, depth + 1), values);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>An actual Count terminal keeps its scalar ordinal instead of reading a private control value.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Recursive_count_materializes_the_completed_result(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await CteStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        var query = context
            .Rows
            .Where(row => row.Id == 1)
            .Select(row => row.Id)
            .WithRecursiveCte(frontier => frontier.Select(value => value + 1),
                rows => rows.Where(value => value > 2),
                4);

        // Act
        var count = await query.CountAsync(safety.Token);

        // Assert
        Assert.Equal(3, count);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>The control row never leaks into an empty visible result, including an outer zero page.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Empty_recursive_payload_has_no_synthetic_materialized_row(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await CteStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        var query = context
            .Rows
            .Where(row => row.Id == -1)
            .Select(row => row.Id)
            .WithRecursiveCte(frontier => frontier.Select(value => value + 1), rows => rows.Take(0), 4);

        // Act
        var values = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Empty(values);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>A bounded identity cycle is a bag; recursion never acquires hidden deduplication.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Bounded_identity_cycle_preserves_all_iterations(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await CteStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        var query = context
            .Rows
            .Where(row => row.Id == 1)
            .Select(row => row.Id)
            .WithRecursiveCte(frontier => frontier, rows => rows, 4);

        // Act
        var values = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([1, 1, 1, 1, 1], values);
    }
}
