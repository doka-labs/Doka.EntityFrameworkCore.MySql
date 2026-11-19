namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlCteGraphIntegrationTests
{
    /// <summary>CTE-P23 keeps a temporal source's instant when expanding a separately stored historical navigation.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(BindingCases))]
    public async Task As_of_source_keeps_historical_reference_values_after_typed_binding(
        string target,
        bool recursive
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        var commands = new GraphCommands();
        await using var context = store.CreateTemporalContext(commands);
        var instant = await SeedHistoricalGraphAsync(context, safety.Token);
        commands.Reads.Clear();
        var source = context.Nodes.TemporalAsOf(instant);
        var query = recursive
            ? source.WithRecursiveCte(frontier => frontier,
                rows => rows.Select(row => new
                {
                    row.Id,
                    row.Target.Name,
                }),
                1)
            : source.WithCte(rows => rows.Select(row => new
            {
                row.Id,
                row.Target.Name,
            }));

        // Act
        var actual = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal(recursive
                ? 2
                : 1,
            actual.Length);

        Assert.All(actual,
            row =>
            {
                Assert.Equal(1, row.Id);
                Assert.Equal("old", row.Name);
            });

        Assert.Single(commands.Reads);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>CTE-P23 follows exact temporal lineage when the bound entity itself came from a navigation projection.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(BindingCases))]
    public async Task Projected_historical_entity_retains_its_instant_for_the_next_navigation(
        string target,
        bool recursive
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        var commands = new GraphCommands();
        await using var context = store.CreateTemporalContext(commands);
        var instant = await SeedHistoricalGraphAsync(context, safety.Token);
        commands.Reads.Clear();
        var source = context
            .Nodes
            .TemporalAsOf(instant)
            .Select(row => row.Target);

        var query = recursive
            ? source.WithRecursiveCte(frontier => frontier, rows => rows.Select(row => row.Next!.Name), 1)
            : source.WithCte(rows => rows.Select(row => row.Next!.Name));

        // Act
        var actual = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal(recursive
                ? 2
                : 1,
            actual.Length);

        Assert.All(actual, value => Assert.Equal("old-next", value));
        Assert.Single(commands.Reads);
    }

    /// <summary>CTE-P23 rehydrates a physical historical entity at the same instant before reference Include.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Historical_scalar_cte_rejoin_and_reference_include_use_the_requested_as_of_instant(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        var commands = new GraphCommands();
        await using var context = store.CreateTemporalContext(commands);
        var instant = await SeedHistoricalGraphAsync(context, safety.Token);
        commands.Reads.Clear();

        // Act
        var actual = await context
            .Nodes
            .TemporalAsOf(instant)
            .Select(row => row.Id)
            .WithCte(ids => context
                .Nodes
                .TemporalAsOf(instant)
                .Where(row => ids.Contains(row.Id))
                .Include(row => row.Target))
            .SingleAsync(safety.Token);

        // Assert
        Assert.Equal(1, actual.Id);
        Assert.Equal(1, actual.TargetId);
        Assert.Equal("old", actual.Target.Name);
        Assert.Single(commands.Reads);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>CTE-N15 rejects incompatible current/history lineage before a command is sent.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(BindingCases))]
    public async Task Current_and_historical_typed_bindings_retain_the_existing_set_operation_guard(
        string target,
        bool recursive
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        var commands = new GraphCommands();
        await using var context = store.CreateTemporalContext(commands);
        var instant = await SeedHistoricalGraphAsync(context, safety.Token);
        commands.Reads.Clear();
        var source = context.Nodes.TemporalAsOf(instant);
        var first = recursive
            ? source.WithRecursiveCte(frontier => frontier, rows => rows, 0)
            : source.WithCte(rows => rows);

        var query = first.Concat(context.Nodes.AsNoTracking());

        // Act
        var failure = await Record.ExceptionAsync(() => query.ToArrayAsync(safety.Token));

        // Assert
        Assert.Contains("temporal",
            Assert
                .IsType<InvalidOperationException>(failure)
                .ToString(),
            StringComparison.OrdinalIgnoreCase);

        Assert.Empty(commands.Reads);
    }

    /// <summary>CTE-N15 rejects two bound temporal instants even if equal CLR types could conceal that difference.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Different_as_of_instants_remain_incompatible_after_typed_binding(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        var commands = new GraphCommands();
        await using var context = store.CreateTemporalContext(commands);
        var instant = await SeedHistoricalGraphAsync(context, safety.Token);
        commands.Reads.Clear();
        var first = context
            .Nodes
            .TemporalAsOf(instant)
            .WithCte(rows => rows);

        var second = context
            .Nodes
            .TemporalAsOf(instant.AddSeconds(1))
            .WithCte(rows => rows);

        // Act
        var failure = await Record.ExceptionAsync(() => first
            .Concat(second)
            .ToArrayAsync(safety.Token));

        // Assert
        Assert.Contains("boundaries",
            Assert
                .IsType<InvalidOperationException>(failure)
                .ToString(),
            StringComparison.OrdinalIgnoreCase);

        Assert.Empty(commands.Reads);
    }
}
