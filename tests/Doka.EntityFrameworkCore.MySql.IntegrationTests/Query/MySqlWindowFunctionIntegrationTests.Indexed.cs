namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlWindowFunctionIntegrationTests
{
    /// <summary>Reports independent tracking variants for each supported pinned engine.</summary>
    public static IEnumerable<TheoryDataRow<string, bool>> IndexedTrackingTargets => SelectedTargets.SelectMany(target
        => new[]
        {
            new TheoryDataRow<string, bool>(target.Data, false), new TheoryDataRow<string, bool>(target.Data, true),
        });

    /// <summary>An actual indexed selector counts only the filtered, ordered source page.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Indexed_selector_starts_at_zero_after_source_filter_and_page(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        var query = context
            .Rows
            .Where(row => row.Amount >= 10m)
            .OrderBy(row => row.Sort)
            .ThenBy(row => row.Id)
            .Skip(1)
            .Take(3)
            .Select((row, index) => new
            {
                row.Id,
                Index = index,
            });

        // Act
        var rows = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([2, 3, 5], rows.Select(row => row.Id));
        Assert.Equal([0, 1, 2], rows.Select(row => row.Index));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>A later filter, reordered page and result materialization retain the original computed index.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Indexed_selector_retains_ordinal_through_later_filter_order_and_page(
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
            .Select((row, index) => new
            {
                row.Id,
                Index = index,
            })
            .Where(row => row.Index >= 2)
            .OrderByDescending(row => row.Id)
            .Skip(1)
            .Take(2);

        // Act
        var rows = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([5, 4], rows.Select(row => row.Id));
        Assert.Equal([4, 3], rows.Select(row => row.Index));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Adding an ordinal retains EF's entity materializer and the caller's explicit tracking choice.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(IndexedTrackingTargets))]
    public async Task Indexed_entity_projection_preserves_explicit_tracking_mode(
        string target,
        bool tracking
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        var source = tracking
            ? context.Rows.AsTracking()
            : context.Rows.AsNoTracking();

        var query = source
            .OrderBy(row => row.Id)
            .Select((row, index) => new
            {
                Entity = row,
                Index = index,
            });

        // Act
        var rows = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([1, 2, 3, 4, 5, 6], rows.Select(row => row.Entity.Id));
        Assert.Equal([0, 1, 2, 3, 4, 5], rows.Select(row => row.Index));
        Assert.Equal([10m, 20m, 30m, 5m, 15m, null], rows.Select(row => row.Entity.Amount));
        Assert.Equal(["alpha", null, "gamma", "delta", "epsilon", "zeta"], rows.Select(row => row.Entity.Name));
        Assert.Equal(tracking
                ? 6
                : 0,
            context
                .ChangeTracker
                .Entries()
                .Count());

        if (tracking)
        {
            Assert.All(rows,
                row => Assert.Same(row.Entity,
                    context
                        .ChangeTracker
                        .Entries<WindowRow>()
                        .Single(entry => entry.Entity.Id == row.Entity.Id)
                        .Entity));
        }
    }

    /// <summary>Partitioned indexed members leave all six original aggregate contracts and NULL semantics intact.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Grouped_indexed_selector_preserves_six_aggregate_results(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        var query = context
            .Rows
            .GroupBy(row => row.Group)
            .OrderBy(group => group.Key)
            .Select(group => new
            {
                group.Key,
                Count = group.Count(),
                LongCount = group.LongCount(),
                Sum = group.Sum(row => row.Amount),
                Average = group.Average(row => row.Amount),
                Minimum = group.Min(row => row.Amount),
                Maximum = group.Max(row => row.Amount),
                Items = group
                    .OrderBy(row => row.Sort)
                    .ThenBy(row => row.Id)
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
        Assert.Equal([1, 2, 3], rows.Select(row => row.Key));
        Assert.Equal([3, 2, 1], rows.Select(row => row.Count));
        Assert.Equal([3L, 2L, 1L], rows.Select(row => row.LongCount));
        Assert.Equal([60m, 20m, 0m], rows.Select(row => row.Sum));
        Assert.Equal([20m, 10m, null], rows.Select(row => row.Average));
        Assert.Equal([10m, 5m, null], rows.Select(row => row.Minimum));
        Assert.Equal([30m, 15m, null], rows.Select(row => row.Maximum));
        Assert.Equal([(1, 0), (2, 1), (3, 2), (4, 0), (5, 1), (6, 0)],
            rows.SelectMany(row => row.Items.Select(item => (item.Id, item.Index))));

        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Filtered member paging resets its index without filtering the original group aggregate.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Grouped_filtered_member_page_keeps_its_own_index_and_aggregate_scope(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        var query = context
            .Rows
            .GroupBy(row => row.Group)
            .OrderBy(group => group.Key)
            .Select(group => new
            {
                group.Key,
                Count = group.Count(),
                SelectedAmount = group
                    .Where(row => row.Amount > 10m)
                    .Sum(row => row.Amount),
                Items = group
                    .Where(row => row.Amount > 10m)
                    .OrderBy(row => row.Id)
                    .Skip(1)
                    .Take(2)
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
        Assert.Equal([1, 2, 3], rows.Select(row => row.Key));
        Assert.Equal([3, 2, 1], rows.Select(row => row.Count));
        Assert.Equal([50m, 15m, 0m], rows.Select(row => row.SelectedAmount));
        Assert.Equal([(3, 0)],
            rows[0]
                .Items
                .Select(item => (item.Id, item.Index)));

        Assert.Empty(rows[1].Items);
        Assert.Empty(rows[2].Items);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Two members of one group retain independent filters, direction and zero-based ordinals.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Grouped_indexed_members_do_not_share_filter_or_ordering_state(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        var query = context
            .Rows
            .GroupBy(row => row.Group)
            .OrderBy(group => group.Key)
            .Select(group => new
            {
                group.Key,
                Forward = group
                    .Where(row => row.Amount > 10m)
                    .OrderBy(row => row.Id)
                    .Select((row, index) => new
                    {
                        row.Id,
                        Index = index,
                    })
                    .ToList(),
                Reverse = group
                    .OrderByDescending(row => row.Id)
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
        Assert.Equal([1, 2, 3], rows.Select(row => row.Key));
        Assert.Equal([(2, 0), (3, 1), (5, 0)],
            rows.SelectMany(row => row.Forward.Select(item => (item.Id, item.Index))));

        Assert.Empty(rows[2].Forward);
        Assert.Equal([(3, 0), (2, 1), (1, 2), (5, 0), (4, 1), (6, 0)],
            rows.SelectMany(row => row.Reverse.Select(item => (item.Id, item.Index))));

        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Every component of a composite grouping key also identifies the member's ranking partition.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Grouped_indexed_selector_preserves_composite_partition_keys(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        var query = context
            .Rows
            .GroupBy(row => new
            {
                row.Group,
                row.Sort,
            })
            .OrderBy(group => group.Key.Group)
            .ThenBy(group => group.Key.Sort)
            .Select(group => new
            {
                group.Key,
                Items = group
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
        Assert.Equal([(1, 1), (1, 2), (2, 1), (2, 3), (3, 1)], rows.Select(row => (row.Key.Group, row.Key.Sort)));
        Assert.Equal([(1, 0), (2, 1), (3, 0), (4, 0), (5, 0), (6, 0)],
            rows.SelectMany(row => row.Items.Select(item => (item.Id, item.Index))));

        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>A converted enum group key keeps its provider representation in the ranking partition and its model value in results.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Grouped_indexed_selector_preserves_converted_key_values_and_partitions(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        var query = context
            .Rows
            .GroupBy(row => row.ConvertedGroup)
            .OrderBy(group => group.Key)
            .Select(group => new
            {
                group.Key,
                Count = group.Count(),
                Items = group
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
        Assert.Equal([WindowGroup.First, WindowGroup.Second, WindowGroup.Third], rows.Select(row => row.Key));
        Assert.Equal([3, 2, 1], rows.Select(row => row.Count));
        Assert.Equal([(1, 0), (2, 1), (3, 2), (4, 0), (5, 1), (6, 0)],
            rows.SelectMany(row => row.Items.Select(item => (item.Id, item.Index))));

        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Whole-group filtering and paging preserve the selected group's complete rows and independent member ordering.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Group_sequence_filter_order_and_page_preserve_member_index_scope(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        var query = context
            .Rows
            .GroupBy(row => row.Group)
            .Where(group => group.Count() > 1)
            .OrderByDescending(group => group.Key)
            .ThenBy(group => group.Count())
            .Skip(1)
            .Take(1)
            .Select(group => new
            {
                group.Key,
                Count = group.Count(),
                Items = group
                    .OrderByDescending(row => row.Id)
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
        var group = Assert.Single(rows);
        Assert.Equal(1, group.Key);
        Assert.Equal(3, group.Count);
        Assert.Equal([(3, 0), (2, 1), (1, 2)], group.Items.Select(item => (item.Id, item.Index)));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>A whole-group aggregate predicate does not become a row filter or discard member rows from that group.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Group_sequence_aggregate_filter_retains_the_complete_selected_group(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        var query = context
            .Rows
            .GroupBy(row => row.Group)
            .Where(group => group.Sum(row => row.Amount) > 25m)
            .OrderBy(group => group.Key)
            .Take(1)
            .Select(group => new
            {
                group.Key,
                Count = group.Count(),
                Items = group
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
        var group = Assert.Single(rows);
        Assert.Equal(1, group.Key);
        Assert.Equal(3, group.Count);
        Assert.Equal([(1, 0), (2, 1), (3, 2)], group.Items.Select(item => (item.Id, item.Index)));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Grouping NULL and empty text remain separate while native case and trailing-space equality stays effective.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Indexed_nullable_text_groups_keep_null_empty_and_collation_members_separate(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "UPDATE `Window rows` SET `Name`=CASE `Id` WHEN 2 THEN '' WHEN 3 THEN ' ' "
            + "WHEN 4 THEN 'alpha' WHEN 5 THEN 'ALPHA ' ELSE NULL END;",
            safety.Token);

        var query = context
            .Rows
            .GroupBy(row => row.Name)
            .Select(group => new
            {
                group.Key,
                Count = group.Count(),
                Items = group
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
        Assert.All(rows, group => Assert.Equal(2, group.Count));
        Assert.All(rows, group => Assert.Equal([0, 1], group.Items.Select(item => item.Index)));
        Assert.Equal([1, 6],
            rows
                .Single(group => group.Key is null)
                .Items
                .Select(item => item.Id));

        Assert.Equal([2, 3],
            rows
                .Single(group => group.Key is not null
                    && group.Key.TrimEnd()
                        .Length
                    == 0)
                .Items
                .Select(item => item.Id));

        Assert.Equal([4, 5],
            rows
                .Single(group => group.Key is not null
                    && group
                        .Key
                        .TrimEnd()
                        .Equals("ALPHA", StringComparison.OrdinalIgnoreCase))
                .Items
                .Select(item => item.Id));

        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>A nullable numeric partition does not merge NULL rows with its CLR zero default.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Indexed_nullable_numeric_groups_do_not_substitute_a_default_key(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "UPDATE `Window rows` SET `Amount`=CASE WHEN `Id`<=2 THEN NULL WHEN `Id`<=4 THEN 0 ELSE 1 END;",
            safety.Token);

        var query = context
            .Rows
            .GroupBy(row => row.Amount)
            .Select(group => new
            {
                group.Key,
                Count = group.Count(),
                Items = group
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
        Assert.All(rows, group => Assert.Equal(2, group.Count));
        Assert.All(rows, group => Assert.Equal([0, 1], group.Items.Select(item => item.Index)));
        Assert.Equal([1, 2],
            rows
                .Single(group => group.Key is null)
                .Items
                .Select(item => item.Id));

        Assert.Equal([3, 4],
            rows
                .Single(group => group.Key == 0m)
                .Items
                .Select(item => item.Id));

        Assert.Equal([5, 6],
            rows
                .Single(group => group.Key == 1m)
                .Items
                .Select(item => item.Id));

        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Ordinary nullable scalar joins keep excluding NULL keys rather than acquiring group membership semantics.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Ordinary_nullable_join_still_excludes_null_keys(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        var query = context
            .Rows
            .Join(context.Rows,
                left => left.Name,
                right => right.Name,
                (left, right) => new
                {
                    Left = left.Id,
                    Right = right.Id,
                })
            .OrderBy(row => row.Left);

        // Act
        var rows = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([(1, 1), (3, 3), (4, 4), (5, 5), (6, 6)], rows.Select(row => (row.Left, row.Right)));
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
