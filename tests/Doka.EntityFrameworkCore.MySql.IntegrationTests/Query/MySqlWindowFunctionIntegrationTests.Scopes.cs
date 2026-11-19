namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlWindowFunctionIntegrationTests
{
    /// <summary>Supplies independent filter opt-out and empty-input cases on each pinned engine.</summary>
    public static TheoryData<string, bool> WindowFlagTargets
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

    /// <summary>Independent partitions in one projection cannot accidentally share a window definition.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Different_partition_definitions_remain_independent_in_one_projection(
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
                ByGroup = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .LongCount(),
                BySort = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Sort)
                    .LongCount(),
                Global = EF
                    .Functions
                    .Window()
                    .OrderBy(row.Id)
                    .RowNumber(),
            });

        // Act
        var rows = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([3L, 3L, 3L, 2L, 2L, 1L], rows.Select(row => row.ByGroup));
        Assert.Equal([4L, 4L, 1L, 4L, 1L, 4L], rows.Select(row => row.BySort));
        Assert.Equal([1L, 2L, 3L, 4L, 5L, 6L], rows.Select(row => row.Global));
    }

    /// <summary>Zero offsets preserve the current NULL or value rather than moving across partitions.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Zero_navigation_offsets_preserve_the_current_nullable_value(
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
                    .Lag(row.Amount, 0),
                Next = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .OrderBy(row.Id)
                    .Lead(row.Amount, 0),
            });

        // Act
        var rows = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([10m, 20m, 30m, 5m, 15m, null], rows.Select(row => row.Previous));
        Assert.Equal([10m, 20m, 30m, 5m, 15m, null], rows.Select(row => row.Next));
    }

    /// <summary>Finite literal RANGE includes value-distance peers while ROWS counts physical neighbors.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Literal_sliding_range_and_rows_keep_distinct_boundary_semantics(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        var mariaDb = IntegrationTestEnvironment.GetServerVersion(Enum.Parse<IntegrationDatabaseTarget>(target))
            .IsMariaDb;

        var query = context
            .Rows
            .OrderBy(row => row.Id)
            .Select(row => new
            {
                Range = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .OrderBy(row.Sort)
                    .RangeBetween(MySqlWindowFrameBound.Preceding(1), MySqlWindowFrameBound.Following(1))
                    .Sum(row.Amount),
                Rows = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .OrderBy(row.Sort)
                    .ThenBy(row.Id)
                    .RowsBetween(MySqlWindowFrameBound.Preceding(1), MySqlWindowFrameBound.Following(1))
                    .Sum(row.Amount),

                // WHY: MariaDB caches computed RANGE boundaries in the ordering key's type.
                // Keep that native integer behavior distinct from explicitly requested decimal arithmetic.
                IntegerKeyFractionalRange = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .OrderBy(row.Sort)
                    .RangeBetween(MySqlWindowFrameBound.Preceding(0.75m), MySqlWindowFrameBound.Following(0.75m))
                    .Sum(row.Amount),
                FractionalRange = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .OrderBy((decimal)row.Sort)
                    .RangeBetween(MySqlWindowFrameBound.Preceding(0.75m), MySqlWindowFrameBound.Following(0.75m))
                    .Sum(row.Amount),
            });

        // Act
        var rows = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([60m, 60m, 60m, 5m, 15m, 0m], rows.Select(row => row.Range));
        Assert.Equal([30m, 60m, 50m, 20m, 20m, 0m], rows.Select(row => row.Rows));
        Assert.Equal(mariaDb
                ? [60m, 60m, 60m, 5m, 15m, 0m]
                : [30m, 30m, 30m, 5m, 15m, 0m],
            rows.Select(row => row.IntegerKeyFractionalRange));

        Assert.Equal([30m, 30m, 30m, 5m, 15m, 0m], rows.Select(row => row.FractionalRange));
    }

    /// <summary>A constant group preserves both its aggregate and member result shape, including no group on empty input.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(WindowFlagTargets))]
    public async Task Constant_group_preserves_native_values_and_empty_result_shape(
        string target,
        bool empty
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        var source = empty
            ? context.Rows.Where(row => false)
            : context.Rows;

        var query = source
            .GroupBy(row => 1)
            .Select(group => new
            {
                Count = group.Count(),
                LongCount = group.LongCount(),
                Sum = group.Sum(row => row.Amount),
                Average = group.Average(row => row.Amount),
                Minimum = group.Min(row => row.Amount),
                Maximum = group.Max(row => row.Amount),
                Members = group
                    .OrderBy(row => row.Id)
                    .Select(row => row.Id)
                    .ToList(),
            });

        // Act
        var rows = await query.ToArrayAsync(safety.Token);

        // Assert
        if (empty)
        {
            Assert.Empty(rows);
        }
        else
        {
            var group = Assert.Single(rows);
            Assert.Equal((6, 6L, 80m, 16m, 5m, 30m),
                (group.Count, group.LongCount, group.Sum, group.Average, group.Minimum, group.Maximum));

            Assert.Equal([1, 2, 3, 4, 5, 6], group.Members);
        }
    }

    /// <summary>Source and independently navigated target filters retain the same opt-out contract around a window.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(WindowFlagTargets))]
    public async Task Root_and_navigation_filters_preserve_explicit_opt_out(
        string target,
        bool ignoreFilters
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "UPDATE `Window rows` SET `SourceVisible`=FALSE WHERE `Id`=2; "
            + "UPDATE `Window targets` SET `IsVisible`=FALSE WHERE `Id`=2;",
            safety.Token);

        var source = ignoreFilters
            ? context.Rows.IgnoreQueryFilters()
            : context.Rows;

        var query = source
            .OrderBy(row => row.Id)
            .Select(row => new
            {
                row.Id,
                Target = row.Target.Label,
                Position = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .OrderBy(row.Id)
                    .RowNumber(),
            });

        // Act
        var rows = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal(ignoreFilters
                ? [1, 2, 3, 4, 5, 6]
                : [1, 3, 6],
            rows.Select(row => row.Id));

        Assert.Equal<string>(ignoreFilters
                ? ["first", "first", "first", "second", "second", "third"]
                : ["first", "first", "third"],
            rows.Select(row => row.Target));

        Assert.Equal(ignoreFilters
                ? [1, 2, 3, 1, 2, 1]
                : [1, 2, 1],
            rows.Select(row => row.Position));
    }

    /// <summary>NULL and case/space-equal SQL partition keys stay peers under the configured collation.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Nullable_collation_equal_partition_keys_preserve_peer_values(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "UPDATE `Window rows` SET `Name`=CASE `Id` WHEN 1 THEN 'alpha' WHEN 2 THEN 'ALPHA ' "
            + "WHEN 5 THEN 'beta' WHEN 6 THEN 'BETA' ELSE NULL END;",
            safety.Token);

        var query = context
            .Rows
            .OrderBy(row => row.Id)
            .Select(row => new
            {
                Count = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Name)
                    .LongCount(),
                Position = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Name)
                    .OrderBy(row.Id)
                    .RowNumber(),
            });

        // Act
        var rows = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([2L, 2L, 2L, 2L, 2L, 2L], rows.Select(row => row.Count));
        Assert.Equal([1L, 2L, 1L, 2L, 1L, 2L], rows.Select(row => row.Position));
    }

    /// <summary>Grouped indexed elements follow database collation equality rather than CLR string equality.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Nullable_collation_equal_group_keys_preserve_complete_member_shapes(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "UPDATE `Window rows` SET `Name`=CASE `Id` WHEN 1 THEN 'alpha' WHEN 2 THEN 'ALPHA ' "
            + "WHEN 5 THEN 'beta' WHEN 6 THEN 'BETA' ELSE NULL END;",
            safety.Token);

        var query = context
            .Rows
            .GroupBy(row => row.Name)
            .Select(group => new
            {
                group.Key,
                Count = group.Count(),
                Members = group
                    .OrderBy(row => row.Id)
                    .Select((row, index) => new
                    {
                        row.Id,
                        Index = index,
                    })
                    .ToList(),
            });

        // Act
        var rows = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal(3, rows.Length);
        var ordered = rows
            .OrderBy(row => row.Members[0].Id)
            .ToArray();

        Assert.Equal([2, 2, 2], ordered.Select(row => row.Count));
        Assert.Equal([1, 2, 3, 4, 5, 6],
            ordered
                .SelectMany(row => row.Members)
                .Select(row => row.Id));

        Assert.All(ordered, row => Assert.Equal([0, 1], row.Members.Select(member => member.Index)));
        Assert.Null(ordered[1].Key);

        // WHY: SQL may choose either equivalent spelling as the representative group key.
        Assert.Equal("ALPHA",
            ordered[0].Key!
                .TrimEnd()
                .ToUpperInvariant());

        Assert.Equal("BETA",
            ordered[2].Key!
                .TrimEnd()
                .ToUpperInvariant());
    }

    /// <summary>Moving a selective filter across window evaluation deliberately changes ordinal values.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(WindowFlagTargets))]
    public async Task Filter_before_and_after_the_window_preserves_the_requested_ordinal(
        string target,
        bool before
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        var source = before
            ? context.Rows.Where(row => row.Amount >= 20m)
            : context.Rows;

        var projected = source.Select(row => new
        {
            row.Id,
            row.Amount,
            Position = EF
                .Functions
                .Window()
                .PartitionBy(row.Group)
                .OrderBy(row.Id)
                .RowNumber(),
        });

        var query = (before
            ? projected
            : projected.Where(row => row.Amount >= 20m)).OrderBy(row => row.Id);

        // Act
        var rows = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([2, 3], rows.Select(row => row.Id));
        Assert.Equal(before
                ? new long[] { 1, 2 }
                : [2L, 3L],
            rows.Select(row => row.Position));
    }

    /// <summary>Widened sums and integer averages retain their CLR contracts at the Int32 boundary.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Numeric_boundary_windows_preserve_explicit_widening_and_average_casts(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM `Window rows`; INSERT INTO `Window rows` (`Id`,`Group`,`Sort`,`Amount`) VALUES "
            + "(2147483646,1,1,99999999999999.9999),(2147483647,1,2,99999999999999.9999);",
            safety.Token);

        var query = context
            .Rows
            .OrderBy(row => row.Id)
            .Select(row => new
            {
                Sum = EF
                    .Functions
                    .Window()
                    .Sum((long)row.Id),
                Average = EF
                    .Functions
                    .Window()
                    .Average(row.Id),
                DecimalSum = EF
                    .Functions
                    .Window()
                    .Sum(row.Amount),
                Count = EF
                    .Functions
                    .Window()
                    .Count(),
            });

        // Act
        var rows = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([4294967293L, 4294967293L], rows.Select(row => row.Sum));
        Assert.Equal([2147483646.5, 2147483646.5], rows.Select(row => row.Average));
        Assert.Equal([199999999999999.9998m, 199999999999999.9998m], rows.Select(row => row.DecimalSum));
        Assert.Equal([2, 2], rows.Select(row => row.Count));
    }

    /// <summary>An Int32 aggregate never silently wraps a native result that exceeds its promised CLR range.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Narrow_window_sum_overflow_remains_visible_at_materialization(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM `Window rows`; INSERT INTO `Window rows` (`Id`,`Group`,`Sort`) VALUES "
            + "(2147483646,1,1),(2147483647,1,2);",
            safety.Token);

        var query = context.Rows.Select(row => EF
            .Functions
            .Window()
            .Sum(row.Id));

        // Act
        var error = await Record.ExceptionAsync(async () => await query.ToArrayAsync(safety.Token));

        // Assert
        Assert.IsType<OverflowException>(error);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }
}
