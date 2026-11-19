namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlCteIntegrationTests
{
    /// <summary>Exercises the native stream and EF-owned buffering as independently reported cases.</summary>
    public static TheoryData<string, bool> BufferCases
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

    /// <summary>Both recursive arms retain their native ordinal placeholder without adding a CLR result member.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(BufferCases))]
    public async Task Empty_recursive_dto_materializes_every_iteration_with_and_without_buffering(
        string target,
        bool buffered
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await CteStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext(buffered);
        var query = context
            .Rows
            .Where(row => row.Id == 1)
            .Select(_ => new { })
            .WithRecursiveCte(frontier => frontier, rows => rows, 3);

        // Act
        var values = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal(4, values.Length);
        Assert.All(values, value => Assert.NotNull(value));
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>An empty anchor cannot hide an unsafe runtime budget in a cached compiled query.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Compiled_empty_recursion_rechecks_the_current_budget_and_remains_reusable_after_failure(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await CteStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        var compiled = EF.CompileAsyncQuery((CteContext database, int depth) => database
            .Rows
            .Where(row => row.Id == -1)
            .Select(row => row.Id)
            .WithRecursiveCte(frontier => frontier.Select(value => value + 1), rows => rows, depth));

        var before = await ReadCompiledAsync(compiled(context, 0), safety.Token);

        // Act
        var failure =
            await Record.ExceptionAsync(() => ReadCompiledAsync(compiled(context, int.MaxValue), safety.Token));

        var after = await ReadCompiledAsync(compiled(context, 1), safety.Token);

        // Assert
        Assert.Empty(before);
        Assert.Contains("below the active server recursion limit",
            Assert.IsType<InvalidOperationException>(failure)
                .Message,
            StringComparison.Ordinal);

        Assert.Empty(after);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Collects a compiled stream with caller cancellation and no hidden query or materialization fallback.</summary>
    private static async Task<int[]> ReadCompiledAsync(
        IAsyncEnumerable<int> source,
        CancellationToken cancellationToken
    )
    {
        var values = new List<int>();

        await foreach (var value in source
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            values.Add(value);
        }

        return values.ToArray();
    }
}
