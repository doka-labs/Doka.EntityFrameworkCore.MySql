namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlInsertSelectIntegrationTests
{
    /// <summary>A compiled terminal rebinds each context, source parameter and required cancellation token.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Compiled_insert_rebinds_context_parameters_and_token_on_each_execution(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        var commands = new InsertCommands();
        await using var first = store.CreateContext(commands);
        await using var second = store.CreateContext(commands);

        // WHY: This EF overload returns the terminal's Task<int> directly; CompileAsyncQuery would add an unwanted Task layer.
        var compiled = EF.CompileQuery((InsertContext context, int minimum, CancellationToken token) => context
            .Set<InsertSource>()
            .Where(row => row.Value > minimum)
            .ExecuteInsertFromQueryAsync(context.Set<InsertTarget>(),
                setters => setters
                    .SetProperty(row => row.Value, row => row.Value)
                    .SetProperty(row => row.Code, row => row.Label),
                token));

        // Act
        var firstCount = await compiled(first, 15, safety.Token);
        var secondCount = await compiled(second, 25, safety.Token);
        var actual = await ReadNativeAsync(store.Observer,
            $"SELECT `Value`,`order` FROM `{TargetTable}` ORDER BY `Value`;",
            safety.Token);

        // Assert
        Assert.Equal(2, firstCount);
        Assert.Equal(1, secondCount);
        Assert.Equal(["20|beta", "30|gamma", "30|gamma"], actual);
        Assert.Equal(2, commands.Statements.Count);
        Assert.Equal(safety.Token, commands.Tokens[0]);
        Assert.Equal(safety.Token, commands.Tokens[1]);
        Assert.Same(first.Database.GetDbConnection(), commands.Connections[0]);
        Assert.Same(second.Database.GetDbConnection(), commands.Connections[1]);
        Assert.Equal(0, commands.Readers);
    }

    /// <summary>Parallel compilations on separate store/model graphs never retain another context's target or database.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Parallel_model_compilations_keep_target_database_and_projection_separate(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var firstStore = await InsertStore.CreateAsync(target, safety.Token);
        await using var secondStore = await InsertStore.CreateAsync(target, safety.Token);
        await using var first = firstStore.CreateContext();
        await using var second = secondStore.CreateContext();

        // Act
        var counts = await Task.WhenAll(
            first
                .Set<InsertSource>()
                .Where(row => row.Id == 1)
                .ExecuteInsertFromQueryAsync(first.Set<InsertTarget>(),
                    setters => setters
                        .SetProperty(row => row.Value, row => row.Value + 1)
                        .SetProperty(row => row.Code, row => "first"),
                    safety.Token),
            second
                .Set<InsertSource>()
                .Where(row => row.Id == 2)
                .ExecuteInsertFromQueryAsync(second.Set<InsertTarget>(),
                    setters => setters
                        .SetProperty(row => row.Value, row => row.Value + 2)
                        .SetProperty(row => row.Code, row => "second"),
                    safety.Token));

        var firstRows = await ReadNativeAsync(firstStore.Observer,
            $"SELECT `Value`,`order` FROM `{TargetTable}`;",
            safety.Token);

        var secondRows = await ReadNativeAsync(secondStore.Observer,
            $"SELECT `Value`,`order` FROM `{TargetTable}`;",
            safety.Token);

        // Assert
        Assert.Equal([1, 1], counts);
        Assert.Equal(["11|first"], firstRows);
        Assert.Equal(["22|second"], secondRows);
        Assert.NotSame(first.Model, second.Model);
    }
}
