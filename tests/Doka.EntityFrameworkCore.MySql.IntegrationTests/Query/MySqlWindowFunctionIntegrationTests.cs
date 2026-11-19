namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Checks native window values, peers, defaults and outer LINQ composition on all supported engines.</summary>
[IntegrationDatabaseTest]
[Trait("VerificationLane", "FullIntegration")]
public sealed partial class MySqlWindowFunctionIntegrationTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Retains native plan evidence without hardware-dependent performance gates.</summary>
    public MySqlWindowFunctionIntegrationTests(
        ITestOutputHelper output
    )
    {
        _output = output;
    }

    /// <summary>Reports each supported pinned engine as an independent theory case.</summary>
    public static TheoryData<string> SelectedTargets => new(IntegrationTestEnvironment
        .GetSelectedTargets()
        .Where(target => IntegrationTestEnvironment
            .GetSupportedTargets()
            .Contains(target))
        .Select(target => target.ToString()));

    /// <summary>Ranking functions distinguish peers from the explicit final result ordering.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Native_ranking_preserves_peer_and_partition_semantics(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        var query = context
            .Rows
            .OrderBy(row => row.Id)
            .Select(row => new
            {
                row.Id,
                Row = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .OrderBy(row.Sort)
                    .ThenBy(row.Id)
                    .RowNumber(),
                Rank = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .OrderBy(row.Sort)
                    .Rank(),
                Dense = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .OrderBy(row.Sort)
                    .DenseRank(),
                Bucket = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .OrderBy(row.Id)
                    .Ntile(2),
            });

        // Act
        var rows = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([1, 2, 3, 4, 5, 6], rows.Select(row => row.Id));
        Assert.Equal([1L, 2L, 3L, 1L, 2L, 1L], rows.Select(row => row.Row));
        Assert.Equal([1L, 1L, 3L, 1L, 2L, 1L], rows.Select(row => row.Rank));
        Assert.Equal([1L, 1L, 2L, 1L, 2L, 1L], rows.Select(row => row.Dense));
        Assert.Equal([1L, 1L, 2L, 1L, 2L, 1L], rows.Select(row => row.Bucket));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>A present NULL neighbor must remain NULL instead of receiving the missing-row default.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Native_navigation_distinguishes_missing_and_present_null_neighbors(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        var fallback = "missing 'quoted'";
        var query = context
            .Rows
            .OrderBy(row => row.Id)
            .Select(row => new
            {
                Previous = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .OrderBy(row.Id)
                    .Lag(row.Name, 1, fallback),
                Next = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .OrderBy(row.Id)
                    .Lead(row.Name, 1, fallback),
            });

        // Act
        var rows = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([fallback, "alpha", null, fallback, "delta", fallback], rows.Select(row => row.Previous));
        Assert.Equal([null, "gamma", fallback, "epsilon", fallback, fallback], rows.Select(row => row.Next));
    }

    /// <summary>Default RANGE peers and explicit ROWS bounds produce different, exact running sums.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Native_aggregate_frames_preserve_peer_and_row_semantics(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        var query = context
            .Rows
            .OrderBy(row => row.Id)
            .Select(row => new
            {
                PeerSum = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .OrderBy(row.Sort)
                    .Sum(row.Amount),
                RowSum = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .OrderBy(row.Sort)
                    .ThenBy(row.Id)
                    .RowsBetween(MySqlWindowFrameBound.UnboundedPreceding, MySqlWindowFrameBound.CurrentRow)
                    .Sum(row.Amount),
                WholeAverage = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .Average(row.Amount),
                Count = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .LongCount(),
            });

        // Act
        var rows = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([30m, 30m, 60m, 5m, 20m, 0m], rows.Select(row => row.PeerSum));
        Assert.Equal([10m, 30m, 60m, 5m, 20m, 0m], rows.Select(row => row.RowSum));
        Assert.Equal([20m, 20m, 20m, 10m, 10m, null], rows.Select(row => row.WholeAverage));
        Assert.Equal([3L, 3L, 3L, 2L, 2L, 1L], rows.Select(row => row.Count));
    }

    /// <summary>An outer predicate filters computed row numbers after window evaluation, rather than before it.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Window_projection_can_be_filtered_and_paged_in_an_outer_scope(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        var query = context
            .Rows
            .Select(row => new
            {
                row.Id,
                Position = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .OrderBy(row.Id)
                    .RowNumber(),
            })
            .Where(row => row.Position > 1)
            .OrderBy(row => row.Id)
            .Take(2);

        // Act
        var rows = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([2, 3], rows.Select(row => row.Id));
        Assert.Equal([2L, 3L], rows.Select(row => row.Position));
    }
}
