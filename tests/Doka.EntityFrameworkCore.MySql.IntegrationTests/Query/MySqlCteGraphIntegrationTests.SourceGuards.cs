namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlCteGraphIntegrationTests
{
    /// <summary>CTE-N11 rejects source grouping objects instead of silently preloading grouped client state.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Client_grouping_source_is_rejected_before_a_native_reader(
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
            .GroupBy(row => row.TargetId)
            .WithCte(groups => groups);

        // Act
        var failure = await Record.ExceptionAsync(() => query.ToArrayAsync(safety.Token));

        // Assert
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Empty(commands.Reads);
        Assert.Empty(commands.Writes);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>CTE-N02 refuses a bound queryable escaping as a deferred projection rather than executing an ordinary unbound query.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Deferred_bound_queryable_output_is_rejected_before_a_native_reader(
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
            .Select(row => row.Id)
            .WithCte(rows =>
                context.Edges.Select(edge => rows.Where(id => id == edge.ParentId)));

        // Act
        var failure = await Record.ExceptionAsync(() => query.ToArrayAsync(safety.Token));

        // Assert
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Empty(commands.Reads);
        Assert.Empty(commands.Writes);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>CTE-N02 rejects foreign execution ownership in the recursive arm before either context can query data.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Foreign_context_recursive_arm_is_rejected_before_either_native_reader(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        var commands = new GraphCommands();
        await using var context = store.CreateContext(commands);
        await using var other = store.CreateContext(commands);
        var query = context
            .Vertices
            .Select(row => row.Id)
            .WithRecursiveCte(
                frontier => frontier.Join(other.Vertices, id => id, row => row.Id, (id, _) => id),
                rows => rows,
                1);

        // Act
        var failure = await Record.ExceptionAsync(() => query.ToArrayAsync(safety.Token));

        // Assert
        Assert.Contains("context",
            Assert.IsType<InvalidOperationException>(failure)
                .Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Empty(commands.Reads);
        Assert.Empty(commands.Writes);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);

        Assert.Equal(ConnectionState.Closed,
            other.Database.GetDbConnection()
                .State);
    }

    /// <summary>CTE-P23 supports a physical entity rejoin with a single-query collection include and exact related rows.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Physical_entity_rejoin_can_include_a_collection_in_the_same_statement(
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
            .Where(row => row.Id == 1)
            .Select(row => row.TargetId)
            .WithCte(ids =>
                context
                    .Targets
                    .Where(row => ids.Contains(row.Id))
                    .Include(row => row.Vertices)
                    .AsSingleQuery());

        // Act
        var targetRow = await query.SingleAsync(safety.Token);

        // Assert
        Assert.Equal(10, targetRow.Id);
        Assert.Equal([1, 4, 5, 6],
            targetRow
                .Vertices
                .OrderBy(row => row.Id)
                .Select(row => row.Id));

        Assert.Single(commands.Reads);
        Assert.Empty(commands.Writes);
    }

    /// <summary>CTE-N11 never decomposes a shared statement binding into an unadvertised split-query preload.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Split_collection_include_is_rejected_before_its_first_native_statement(
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
            .Where(row => row.Id == 1)
            .Select(row => row.TargetId)
            .WithCte(ids =>
                context
                    .Targets
                    .Where(row => ids.Contains(row.Id))
                    .Include(row => row.Vertices)
                    .AsSplitQuery());

        // Act
        var failure = await Record.ExceptionAsync(() => query.ToArrayAsync(safety.Token));

        // Assert
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Empty(commands.Reads);
        Assert.Empty(commands.Writes);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }
}
