namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlWindowFunctionIntegrationTests
{
    /// <summary>Separates an empty preceding frame from a current-row all-NULL frame on every engine.</summary>
    public static IEnumerable<TheoryDataRow<string, bool>> NullableFrameTargets => SelectedTargets.SelectMany(target
        => new[]
        {
            new TheoryDataRow<string, bool>(target.Data, false), new TheoryDataRow<string, bool>(target.Data, true),
        });

    /// <summary>Full-frame value functions respect present NULLs and missing one-based positions.</summary>
    /// <param name="target">The selected pinned engine.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Native_value_functions_preserve_full_frame_null_and_position_semantics(
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
                First = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .OrderBy(row.Id)
                    .RowsBetween(MySqlWindowFrameBound.UnboundedPreceding, MySqlWindowFrameBound.UnboundedFollowing)
                    .FirstValue(row.Name),
                Last = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .OrderBy(row.Id)
                    .RowsBetween(MySqlWindowFrameBound.UnboundedPreceding, MySqlWindowFrameBound.UnboundedFollowing)
                    .LastValue(row.Name),
                Second = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .OrderBy(row.Id)
                    .RowsBetween(MySqlWindowFrameBound.UnboundedPreceding, MySqlWindowFrameBound.UnboundedFollowing)
                    .NthValue(row.Name, 2),
                Count = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .LongCount(),
            });

        // Act
        var rows = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal(["alpha", "alpha", "alpha", "delta", "delta", "zeta"], rows.Select(row => row.First));
        Assert.Equal(["gamma", "gamma", "gamma", "epsilon", "epsilon", "zeta"], rows.Select(row => row.Last));
        Assert.Equal([null, null, null, "epsilon", "epsilon", null], rows.Select(row => row.Second));
        Assert.Equal([3L, 3L, 3L, 2L, 2L, 1L], rows.Select(row => row.Count));
    }

    /// <summary>Empty frames and present NULL values remain distinguishable by their actual frame cardinality.</summary>
    /// <param name="target">The selected pinned engine.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Native_value_functions_preserve_empty_and_null_valued_frames(
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
                First = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .OrderBy(row.Id)
                    .RowsBetween(MySqlWindowFrameBound.Preceding(2), MySqlWindowFrameBound.Preceding(1))
                    .FirstValue(row.Name),
                Last = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .OrderBy(row.Id)
                    .RowsBetween(MySqlWindowFrameBound.Preceding(2), MySqlWindowFrameBound.Preceding(1))
                    .LastValue(row.Name),
                Second = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .OrderBy(row.Id)
                    .RowsBetween(MySqlWindowFrameBound.Preceding(2), MySqlWindowFrameBound.Preceding(1))
                    .NthValue(row.Name, 2),
                Count = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .OrderBy(row.Id)
                    .RowsBetween(MySqlWindowFrameBound.Preceding(2), MySqlWindowFrameBound.Preceding(1))
                    .LongCount(),
            });

        // Act
        var rows = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([null, "alpha", "alpha", null, "delta", null], rows.Select(row => row.First));
        Assert.Equal([null, "alpha", null, null, "delta", null], rows.Select(row => row.Last));
        Assert.All(rows, row => Assert.Null(row.Second));
        Assert.Equal([0L, 1L, 2L, 0L, 1L, 0L], rows.Select(row => row.Count));
    }

    /// <summary>Native distributions preserve peer cardinality and the single-row partition boundary.</summary>
    /// <param name="target">The selected pinned engine.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Native_distribution_functions_preserve_peers_and_single_row_partitions(
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
                Percent = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .OrderBy(row.Sort)
                    .PercentRank(),
                OrderedPercent = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .OrderBy(row.Id)
                    .PercentRank(),
                Cumulative = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .OrderBy(row.Sort)
                    .CumeDist(),
            });

        // WHY: MariaDB declares ten decimals for distribution result fields, including prepared reads.
        // MySQL retains the existing twelve-place oracle; both still check the exact expected peer fraction.
        var precision = IntegrationTestEnvironment.GetServerVersion(Enum.Parse<IntegrationDatabaseTarget>(target))
            .IsMariaDb
            ? 10
            : 12;

        var twoThirds = Math.Round(2d / 3d, precision);

        // Act
        var rows = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([0d, 0d, 1d, 0d, 1d, 0d], rows.Select(row => row.Percent));
        Assert.Equal([0d, 0.5d, 1d, 0d, 1d, 0d], rows.Select(row => row.OrderedPercent));
        Assert.Equal([twoThirds, twoThirds, 1d, 0.5d, 1d, 1d],
            rows.Select(row => Math.Round(row.Cumulative, precision)));
    }

    /// <summary>The implicit adjacent-row overload has the same native values as an explicit offset of one.</summary>
    /// <param name="target">The selected pinned engine.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Native_implicit_navigation_offsets_preserve_nullable_neighbor_values(
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
                Previous = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .OrderBy(row.Id)
                    .Lag(row.Amount),
                Next = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .OrderBy(row.Id)
                    .Lead(row.Amount),
                ExplicitPrevious = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .OrderBy(row.Id)
                    .Lag(row.Amount, 1),
                ExplicitNext = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .OrderBy(row.Id)
                    .Lead(row.Amount, 1),
            });

        // Act
        var rows = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([null, 10m, 20m, null, 5m, null], rows.Select(row => row.Previous));
        Assert.Equal([20m, 30m, null, 15m, null, null], rows.Select(row => row.Next));
        Assert.Equal(rows.Select(row => row.ExplicitPrevious), rows.Select(row => row.Previous));
        Assert.Equal(rows.Select(row => row.ExplicitNext), rows.Select(row => row.Next));
    }

    /// <summary>Nullable aggregates distinguish an empty frame from a present all-NULL frame.</summary>
    /// <param name="target">The selected pinned engine.</param>
    /// <param name="currentFrame">Whether the frame contains the current row instead of preceding rows.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(NullableFrameTargets))]
    public async Task Native_nullable_aggregates_preserve_empty_and_all_null_frame_semantics(
        string target,
        bool currentFrame
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        var query = currentFrame
            ? context
                .Rows
                .OrderBy(row => row.Id)
                .Select(row => new
                {
                    Sum = EF
                        .Functions
                        .Window()
                        .PartitionBy(row.Group)
                        .OrderBy(row.Id)
                        .RowsBetween(MySqlWindowFrameBound.CurrentRow, MySqlWindowFrameBound.CurrentRow)
                        .Sum(row.Amount),
                    Average = EF
                        .Functions
                        .Window()
                        .PartitionBy(row.Group)
                        .OrderBy(row.Id)
                        .RowsBetween(MySqlWindowFrameBound.CurrentRow, MySqlWindowFrameBound.CurrentRow)
                        .Average(row.Amount),
                    Minimum = EF
                        .Functions
                        .Window()
                        .PartitionBy(row.Group)
                        .OrderBy(row.Id)
                        .RowsBetween(MySqlWindowFrameBound.CurrentRow, MySqlWindowFrameBound.CurrentRow)
                        .Min(row.Amount),
                    Maximum = EF
                        .Functions
                        .Window()
                        .PartitionBy(row.Group)
                        .OrderBy(row.Id)
                        .RowsBetween(MySqlWindowFrameBound.CurrentRow, MySqlWindowFrameBound.CurrentRow)
                        .Max(row.Amount),
                    Count = EF
                        .Functions
                        .Window()
                        .PartitionBy(row.Group)
                        .OrderBy(row.Id)
                        .RowsBetween(MySqlWindowFrameBound.CurrentRow, MySqlWindowFrameBound.CurrentRow)
                        .Count(),
                })
            : context
                .Rows
                .OrderBy(row => row.Id)
                .Select(row => new
                {
                    Sum = EF
                        .Functions
                        .Window()
                        .PartitionBy(row.Group)
                        .OrderBy(row.Id)
                        .RowsBetween(MySqlWindowFrameBound.Preceding(2), MySqlWindowFrameBound.Preceding(1))
                        .Sum(row.Amount),
                    Average = EF
                        .Functions
                        .Window()
                        .PartitionBy(row.Group)
                        .OrderBy(row.Id)
                        .RowsBetween(MySqlWindowFrameBound.Preceding(2), MySqlWindowFrameBound.Preceding(1))
                        .Average(row.Amount),
                    Minimum = EF
                        .Functions
                        .Window()
                        .PartitionBy(row.Group)
                        .OrderBy(row.Id)
                        .RowsBetween(MySqlWindowFrameBound.Preceding(2), MySqlWindowFrameBound.Preceding(1))
                        .Min(row.Amount),
                    Maximum = EF
                        .Functions
                        .Window()
                        .PartitionBy(row.Group)
                        .OrderBy(row.Id)
                        .RowsBetween(MySqlWindowFrameBound.Preceding(2), MySqlWindowFrameBound.Preceding(1))
                        .Max(row.Amount),
                    Count = EF
                        .Functions
                        .Window()
                        .PartitionBy(row.Group)
                        .OrderBy(row.Id)
                        .RowsBetween(MySqlWindowFrameBound.Preceding(2), MySqlWindowFrameBound.Preceding(1))
                        .Count(),
                });

        decimal?[] expectedSum = currentFrame
            ? [10m, 20m, 30m, 5m, 15m, 0m]
            : [0m, 10m, 30m, 0m, 5m, 0m];

        decimal?[] expectedAverage = currentFrame
            ? [10m, 20m, 30m, 5m, 15m, null]
            : [null, 10m, 15m, null, 5m, null];

        decimal?[] expectedMinimum = currentFrame
            ? [10m, 20m, 30m, 5m, 15m, null]
            : [null, 10m, 10m, null, 5m, null];

        decimal?[] expectedMaximum = currentFrame
            ? [10m, 20m, 30m, 5m, 15m, null]
            : [null, 10m, 20m, null, 5m, null];

        int[] expectedCount = currentFrame
            ? [1, 1, 1, 1, 1, 1]
            : [0, 1, 2, 0, 1, 0];

        // Act
        var rows = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal(expectedSum, rows.Select(row => row.Sum));
        Assert.Equal(expectedAverage, rows.Select(row => row.Average));
        Assert.Equal(expectedMinimum, rows.Select(row => row.Minimum));
        Assert.Equal(expectedMaximum, rows.Select(row => row.Maximum));
        Assert.Equal(expectedCount, rows.Select(row => row.Count));
    }
}
