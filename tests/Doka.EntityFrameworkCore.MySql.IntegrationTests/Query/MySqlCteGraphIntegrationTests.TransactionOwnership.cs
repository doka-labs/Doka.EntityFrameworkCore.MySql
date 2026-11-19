namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlCteGraphIntegrationTests
{
    /// <summary>CTE-P26/N22 keep the caller's transaction and earlier uncommitted work intact after early reader disposal.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(BindingCases))]
    public async Task Early_reader_disposal_keeps_the_callers_transaction_and_uncommitted_work_owned_by_the_caller(
        string target,
        bool recursive
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        var commands = new GraphCommands();
        await using var context = store.CreateContext(commands, pooled: false);
        await context.Database.OpenConnectionAsync(safety.Token);
        await using var transaction = await context.Database.BeginTransactionAsync(safety.Token);
        await context.Database.ExecuteSqlRawAsync("UPDATE GraphVertices SET Name='caller-owned' WHERE Id=1;",
            safety.Token);

        commands.Writes.Clear();
        var source = context.Facets.Select(row => row.Id);
        var query = recursive
            ? source.WithRecursiveCte(frontier => frontier, rows => rows.OrderBy(id => id), 2)
            : source.WithCte(rows => rows
                .Concat(rows)
                .OrderBy(id => id));

        bool moved;
        int first;

        // Act
        await using (var iterator = query
            .AsAsyncEnumerable()
            .GetAsyncEnumerator(safety.Token))
        {
            moved = await iterator.MoveNextAsync();
            first = iterator.Current;
        }

        var retained = context.Database.CurrentTransaction;
        var ownValue = await context
            .Vertices
            .Where(row => row.Id == 1)
            .Select(row => row.Name)
            .SingleAsync(safety.Token);

        var outsideValue = await store.ReadScalarAsync("SELECT Name FROM GraphVertices WHERE Id=1;", safety.Token);

        // Assert
        Assert.True(moved);
        Assert.Equal(1, first);
        Assert.Same(transaction, retained);
        Assert.Equal(ConnectionState.Open,
            context.Database.GetDbConnection()
                .State);

        Assert.Equal("caller-owned", ownValue);
        Assert.Equal("one", outsideValue);
        Assert.Equal(2, commands.Reads.Count);
        Assert.Empty(commands.Writes);
    }
}
