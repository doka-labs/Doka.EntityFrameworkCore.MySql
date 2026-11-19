namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlWindowFunctionIntegrationTests
{
    /// <summary>Supplies the three independently bound read sources on every native engine.</summary>
    public static TheoryData<string, string> WindowBindingTargets
    {
        get
        {
            var cases = new TheoryData<string, string>();

            foreach (var target in SelectedTargets)
            {
                cases.Add(target, "raw");
                cases.Add(target, "cte");
                cases.Add(target, "materialized");
            }

            return cases;
        }
    }

    /// <summary>Supplies the qualified MySQL correlated-derived-table targets.</summary>
    public static TheoryData<string> CorrelatedWindowTargets
    {
        get
        {
            var cases = new TheoryData<string>();

            foreach (var target in SelectedTargets)
            {
                if (!IntegrationTestEnvironment
                    .GetServerVersion(Enum.Parse<IntegrationDatabaseTarget>(target))
                    .IsMariaDb)
                {
                    cases.Add(target);
                }
            }

            return cases;
        }
    }

    /// <summary>Supplies MariaDB targets whose existing JOIN grammar rejects this correlated derived-table shape.</summary>
    public static TheoryData<string> UnsupportedCorrelatedWindowTargets
    {
        get
        {
            var cases = new TheoryData<string>();

            foreach (var target in SelectedTargets)
            {
                if (IntegrationTestEnvironment
                    .GetServerVersion(Enum.Parse<IntegrationDatabaseTarget>(target))
                    .IsMariaDb)
                {
                    cases.Add(target);
                }
            }

            return cases;
        }
    }

    /// <summary>Raw, ordinary CTE and explicit materialized sources retain their own selected rows before ranking.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(WindowBindingTargets))]
    public async Task Bound_read_source_preserves_window_scope_and_selected_values(
        string target,
        string binding
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        var probe = new WindowParameterCommands();
        await using var context = new WindowContext(store.CreateOptions(probe, false));
        var selected = context.Rows.Where(row => row.Id != 2);
        var query = binding switch
        {
            "raw" => context
                .Rows
                .FromSqlRaw("SELECT * FROM `Window rows` WHERE `Id` <> 2")
                .Select(row => new
                {
                    row.Id,
                    Position = EF
                        .Functions
                        .Window()
                        .PartitionBy(row.Group)
                        .OrderBy(row.Id)
                        .RowNumber(),
                }),
            "cte" => selected.WithCte(shared => shared
                .Select(row => new
                {
                    row.Id,
                    Position = EF
                        .Functions
                        .Window()
                        .PartitionBy(row.Group)
                        .OrderBy(row.Id)
                        .RowNumber(),
                })),
            "materialized" => selected.WithMaterializedSource(shared => shared
                .Select(row => new
                {
                    row.Id,
                    Position = EF
                        .Functions
                        .Window()
                        .PartitionBy(row.Group)
                        .OrderBy(row.Id)
                        .RowNumber(),
                })),
            _ => throw new ArgumentOutOfRangeException(nameof(binding)),
        };

        // Act
        var rows = await query
            .OrderBy(row => row.Id)
            .ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([1, 3, 4, 5, 6], rows.Select(row => row.Id));
        Assert.Equal([1L, 2L, 1L, 2L, 1L], rows.Select(row => row.Position));
        Assert.Equal(1, probe.Readers);
    }

    /// <summary>A joined source keeps all duplicate rows before the window numbers the resulting relation.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Joined_duplicates_are_numbered_on_the_complete_join_relation(
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
                left => left.Group,
                right => right.Group,
                (left, right) => new
                {
                    Left = left.Id,
                    Right = right.Id,
                    Position = EF
                        .Functions
                        .Window()
                        .PartitionBy(left.Group)
                        .OrderBy(left.Id)
                        .ThenBy(right.Id)
                        .RowNumber(),
                })
            .OrderBy(row => row.Left)
            .ThenBy(row => row.Right);

        var groups = new[] { new[] { 1, 2, 3 }, new[] { 4, 5 }, new[] { 6 } };
        var expected = groups
            .SelectMany(ids => ids
                .SelectMany(left => ids.Select(right => (Left: left, Right: right)))
                .Select((pair, index) => (pair.Left, pair.Right, Position: (long)index + 1)))
            .ToArray();

        // Act
        var rows = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal(14, rows.Length);
        Assert.Equal(expected, rows.Select(row => (row.Left, row.Right, row.Position)));
    }

    /// <summary>Distinct before and after numbering deliberately select different relations.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(WindowFlagTargets))]
    public async Task Distinct_window_boundary_preserves_the_requested_relation(
        string target,
        bool distinctFirst
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        var query = distinctFirst
            ? context
                .Rows
                .Select(row => row.Group)
                .Distinct()
                .Select(group => new
                {
                    Group = group,
                    Position = EF
                        .Functions
                        .Window()
                        .OrderBy(group)
                        .RowNumber(),
                })
            : context
                .Rows
                .Select(row => new
                {
                    row.Group,
                    Position = EF
                        .Functions
                        .Window()
                        .OrderBy(row.Id)
                        .RowNumber(),
                })
                .Distinct();

        // Act
        var rows = await query
            .OrderBy(row => row.Position)
            .ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal(distinctFirst
                ? [1, 2, 3]
                : [1, 1, 1, 2, 2, 3],
            rows.Select(row => row.Group));

        Assert.Equal(Enumerable
                .Range(1,
                    distinctFirst
                        ? 3
                        : 6)
                .Select(value => (long)value),
            rows.Select(row => row.Position));
    }

    /// <summary>Set deduplication finishes before global window evaluation and cannot multiply its input.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Union_finishes_before_the_window_counts_and_numbers_its_rows(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        var source = context
            .Rows
            .Where(row => row.Id <= 3)
            .Select(row => row.Group)
            .Union(context
                .Rows
                .Where(row => row.Id >= 3)
                .Select(row => row.Group));

        var query = source
            .Select(group => new
            {
                Group = group,
                Count = EF
                    .Functions
                    .Window()
                    .LongCount(),
                Position = EF
                    .Functions
                    .Window()
                    .OrderBy(group)
                    .RowNumber(),
            })
            .OrderBy(row => row.Group);

        // Act
        var rows = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([1, 2, 3], rows.Select(row => row.Group));
        Assert.Equal([3L, 3L, 3L], rows.Select(row => row.Count));
        Assert.Equal([1L, 2L, 3L], rows.Select(row => row.Position));
    }

    /// <summary>Unused scalar windows are pruned without removing the final selected entity keys.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Unused_window_projection_is_pruned_without_changing_the_selected_keys(
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
                Unused = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .OrderBy(row.Id)
                    .RowNumber(),
            })
            .Select(row => row.Id)
            .OrderBy(id => id);

        var sql = query.ToQueryString();

        // Act
        var rows = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([1, 2, 3, 4, 5, 6], rows);
        Assert.DoesNotContain("ROW_NUMBER", sql, StringComparison.Ordinal);
    }

    /// <summary>MySQL preserves the correlation-before-window collection scope without one command per parent.</summary>
    [RequiresDatabaseTargetsTheory(IntegrationDatabaseTarget.MySql84, IntegrationDatabaseTarget.MySql97)]
    [MemberData(nameof(CorrelatedWindowTargets))]
    public async Task Correlated_split_window_keeps_its_qualified_mysql_scope_without_n_plus_one(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        var probe = new WindowParameterCommands();
        await using var context = new WindowContext(store.CreateOptions(probe, false));
        var query = context
            .Rows
            .AsSplitQuery()
            .OrderBy(row => row.Id)
            .Select(row => new
            {
                row.Id,
                Members = context
                    .Rows
                    .Where(member => member.Group == row.Group)
                    .OrderBy(member => member.Id)
                    .Select(member => new
                    {
                        member.Id,
                        Position = EF
                            .Functions
                            .Window()
                            .PartitionBy(member.Group)
                            .OrderBy(member.Id)
                            .RowNumber(),
                    })
                    .ToList(),
            });

        // Act
        var rows = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([1, 2, 3, 4, 5, 6], rows.Select(row => row.Id));
        Assert.Equal(2, probe.Readers);
        foreach (var row in rows)
        {
            var ids = row.Id <= 3
                ? new[] { 1, 2, 3 }
                : row.Id <= 5
                    ? [4, 5]
                    : [6];

            Assert.Equal(ids, row.Members.Select(member => member.Id));
            Assert.Equal(Enumerable
                    .Range(1, ids.Length)
                    .Select(value => (long)value),
                row.Members.Select(member => member.Position));
        }
    }

    /// <summary>Records the ordinary split-collection contract independently of any new window fence.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Ordinary_split_collection_preserves_its_existing_correlated_scope(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        var probe = new WindowParameterCommands();
        await using var context = new WindowContext(store.CreateOptions(probe, false));
        var query = context
            .Rows
            .AsSplitQuery()
            .OrderBy(row => row.Id)
            .Select(row => new
            {
                row.Id,
                Members = context
                    .Rows
                    .Where(member => member.Group == row.Group)
                    .OrderBy(member => member.Id)
                    .Select(member => member.Id)
                    .ToList(),
            });

        // Act
        var rows = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([1, 2, 3, 4, 5, 6], rows.Select(row => row.Id));
        Assert.Equal(2, probe.Readers);
        foreach (var row in rows)
        {
            var ids = row.Id <= 3
                ? new[] { 1, 2, 3 }
                : row.Id <= 5
                    ? [4, 5]
                    : [6];

            Assert.Equal(ids, row.Members);
        }
    }

    /// <summary>MariaDB reports the existing engine boundary rather than executing an unqualified correlated window scope.</summary>
    [RequiresDatabaseTargetsTheory(IntegrationDatabaseTarget.MariaDb1011,
        IntegrationDatabaseTarget.MariaDb114,
        IntegrationDatabaseTarget.MariaDb118,
        IntegrationDatabaseTarget.MariaDb123)]
    [MemberData(nameof(UnsupportedCorrelatedWindowTargets))]
    public async Task Correlated_split_window_reports_the_mariadb_lateral_boundary(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        var probe = new WindowParameterCommands();
        await using var context = new WindowContext(store.CreateOptions(probe, false));
        var query = context
            .Rows
            .AsSplitQuery()
            .OrderBy(row => row.Id)
            .Select(row => new
            {
                row.Id,
                Members = context
                    .Rows
                    .Where(member => member.Group == row.Group)
                    .OrderBy(member => member.Id)
                    .Select(member => new
                    {
                        member.Id,
                        Position = EF
                            .Functions
                            .Window()
                            .PartitionBy(member.Group)
                            .OrderBy(member.Id)
                            .RowNumber(),
                    })
                    .ToList(),
            });

        // Act
        var error = await Record.ExceptionAsync(async () => await query.ToArrayAsync(safety.Token));

        // Assert
        Assert.Contains("MDB-CORRELATED-DERIVED-TABLE",
            Assert.IsType<InvalidOperationException>(error)
                .Message,
            StringComparison.Ordinal);

        // WHY: Split execution can read its parent command before generating the unsupported
        // child command. This is an engine boundary, not a claim of zero parent reads.
        Assert.InRange(probe.Readers, 0, 1);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Completing the partitioned window relation before correlation is qualified for split collections on every engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Completed_window_relation_preserves_split_members_on_every_engine(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        var probe = new WindowParameterCommands();
        await using var context = new WindowContext(store.CreateOptions(probe, false));

        // WHY: Ranking the complete Group partitions first preserves membership while
        // keeping the later group correlation outside the window's relational boundary.
        var ranked = context.Rows.Select(member => new
        {
            member.Id,
            member.Group,
            Position = EF
                .Functions
                .Window()
                .PartitionBy(member.Group)
                .OrderBy(member.Id)
                .RowNumber(),
        });

        var query = context
            .Rows
            .AsSplitQuery()
            .OrderBy(row => row.Id)
            .Select(row => new
            {
                row.Id,
                Members = ranked
                    .Where(member => member.Group == row.Group)
                    .OrderBy(member => member.Id)
                    .Select(member => new
                    {
                        member.Id,
                        member.Position,
                    })
                    .ToList(),
            });

        // Act
        var rows = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([1, 2, 3, 4, 5, 6], rows.Select(row => row.Id));
        Assert.Equal(2, probe.Readers);
        foreach (var row in rows)
        {
            var ids = row.Id <= 3
                ? new[] { 1, 2, 3 }
                : row.Id <= 5
                    ? [4, 5]
                    : [6];

            Assert.Equal(ids, row.Members.Select(member => member.Id));
            Assert.Equal(Enumerable
                    .Range(1, ids.Length)
                    .Select(value => (long)value),
                row.Members.Select(member => member.Position));
        }
    }
}
