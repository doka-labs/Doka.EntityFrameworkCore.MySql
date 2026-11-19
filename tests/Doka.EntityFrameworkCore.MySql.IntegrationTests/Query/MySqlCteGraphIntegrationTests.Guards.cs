namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlCteGraphIntegrationTests
{
    /// <summary>CTE-P14 retains exact Unicode when different physical columns have genuinely compatible facets.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Different_compatible_physical_columns_preserve_recursive_text_values(
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
            .Select(row => row.Name)
            .WithRecursiveCte(
                frontier => frontier.SelectMany(_ => context
                    .Facets
                    .Where(row => row.Id == 1)
                    .Select(row => row.Label)),
                rows => rows,
                1);

        // Act
        var values = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal<string>(["one", "\u00e9\U0001f600"], values.Order(StringComparer.Ordinal));
        Assert.Single(commands.Reads);
        Assert.Empty(commands.Writes);
    }

    /// <summary>CTE-N06 must not silently copy incompatible physical text into a narrower-encoding recursive anchor.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Different_physical_charsets_do_not_silently_corrupt_a_recursive_text_copy(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        var commands = new GraphCommands();
        await using var context = store.CreateContext(commands, pooled: false);
        await context.Database.OpenConnectionAsync(safety.Token);

        // WHY: A strict-only fixture could hide the data-loss class behind a native conversion error.
        // This nonpooled test owns the session; production CTE execution must not change it.
        await context.Database.ExecuteSqlRawAsync("SET SESSION sql_mode='';", safety.Token);
        commands.Writes.Clear();
        var query = context
            .LatinRows
            .Where(row => row.Id == 1)
            .Select(row => row.Value)
            .WithRecursiveCte(
                frontier => frontier.SelectMany(_ => context
                    .Facets
                    .Where(row => row.Id == 1)
                    .Select(row => row.Label)),
                rows => rows,
                1);

        string[] values = [];

        // Act
        var failure = await Record.ExceptionAsync(async () => values = await query.ToArrayAsync(safety.Token));

        // Assert
        Assert.True(failure is InvalidOperationException,
            "Incompatible recursive physical text needs a provider rejection; observed="
            + (failure?.GetType()
                    .Name
                ?? "success")
            + "; values="
            + string.Join('|', values));

        Assert.Empty(commands.Reads);
        Assert.Empty(commands.Writes);
    }
}
