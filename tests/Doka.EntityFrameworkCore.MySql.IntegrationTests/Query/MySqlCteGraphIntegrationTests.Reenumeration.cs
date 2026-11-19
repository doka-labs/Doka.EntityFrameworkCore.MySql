namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlCteGraphIntegrationTests
{
    /// <summary>CTE-N12 observes valid ordinary volatile values without promising equality, inequality or forced materialization.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Ordinary_volatile_reference_values_follow_native_optimizer_semantics_without_a_fence(
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
            .Select(row => new
            {
                row.Id,
                Token = Guid.NewGuid(),
            })
            .WithCte(rows => rows.Join(rows,
                left => left.Id,
                right => right.Id,
                (left, right) => new
                {
                    left.Id,
                    First = left.Token,
                    Second = right.Token,
                }))
            .OrderBy(row => row.Id);

        // Act
        var values = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([1, 2], values.Select(row => row.Id));
        Assert.All(values,
            row =>
            {
                Assert.NotEqual(Guid.Empty, row.First);
                Assert.NotEqual(Guid.Empty, row.Second);
            });

        var sql = Assert.Single(commands.Reads)
            .Sql;

        Assert.Equal(1, CountOrdinaryDefinitions(sql));
        Assert.DoesNotContain("WHERE 1 = 0", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("WITH RECURSIVE", sql, StringComparison.Ordinal);
        _output.WriteLine(
            "Ordinary volatile native equal-reference observations: {0}/{1}.",
            values.Count(row => row.First == row.Second),
            values.Length);
    }

    /// <summary>CTE-N23 re-enumerates the same deferred query after both captured input and context filter state change.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Reenumerating_the_same_query_rebinds_current_values_without_cached_bound_data(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        var commands = new GraphCommands();
        await using var context = store.CreateContext(commands);
        var maximum = 2;
        var query = context
            .Vertices
            .Where(row => row.Id <= maximum)
            .Select(row => row.Id)
            .WithCte(rows => rows.OrderBy(id => id));

        var before = await query.ToArrayAsync(safety.Token);
        commands.Reads.Clear();

        // Act
        maximum = 4;
        context.TenantId = 2;
        var after = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([1, 2], before);
        Assert.Equal([3], after);
        Assert.Single(commands.Reads);
        Assert.Empty(commands.Writes);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
