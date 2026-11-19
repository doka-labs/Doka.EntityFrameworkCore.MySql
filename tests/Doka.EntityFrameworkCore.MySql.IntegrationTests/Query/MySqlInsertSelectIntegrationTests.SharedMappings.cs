namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlInsertSelectIntegrationTests
{
    /// <summary>Reports complete, conflicting and incomplete shared-column assignments separately.</summary>
    public static IEnumerable<TheoryDataRow<string, string>> SharedMappingCases => SelectedTargets.SelectMany(target
        => new[]
        {
            new TheoryDataRow<string, string>(target.Data, "complete"),
            new TheoryDataRow<string, string>(target.Data, "conflict"),
            new TheoryDataRow<string, string>(target.Data, "missing-companion"),
        });

    /// <summary>One-table ownership accepts a complete shared representation and rejects conflicting or missing companion data.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SharedMappingCases))]
    public async Task Shared_columns_require_consistent_values_and_complete_companion_columns(
        string target,
        string shape
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        var commands = new InsertCommands();
        await using var context = store.CreateContext(commands);
        var count = 0;

        // Act
        var error = await Record.ExceptionAsync(async () => count = await context
            .Set<InsertSource>()
            .Where(row => row.Id <= 2)
            .ExecuteInsertFromQueryAsync(context.Set<SharedTarget>(),
                setters =>
                {
                    setters.SetProperty(row => row.Code, row => row.Label);

                    if (shape != "missing-companion")
                    {
                        if (shape == "conflict")
                        {
                            setters.SetProperty(row => row.Details.Code, row => row.Label + "conflict");
                        }
                        else
                        {
                            setters.SetProperty(row => row.Details.Code, row => row.Label);
                        }

                        setters.SetProperty(row => row.Details.Note, row => "owned companion");
                    }
                },
                safety.Token));

        var actual = await ReadNativeAsync(store.Observer,
            "SELECT `SharedCode`,`CompanionNote` FROM InsertShared ORDER BY `SharedCode`;",
            safety.Token);

        // Assert
        if (shape == "complete")
        {
            Assert.Null(error);
            Assert.Equal(2, count);
            Assert.Equal(["alpha|owned companion", "beta|owned companion"], actual);
            Assert.Single(commands.Statements);
        }
        else
        {
            var rejected = Assert.IsType<InvalidOperationException>(error);
            Assert.Contains(shape == "conflict"
                    ? "conflicting assignments for shared column"
                    : "requires a value for physical column",
                rejected.Message,
                StringComparison.OrdinalIgnoreCase);

            Assert.Empty(actual);
            Assert.Empty(commands.Statements);
        }

        Assert.Equal(0, commands.Readers);
    }

    /// <summary>A modeled nontransactional engine is rejected before invoking even a physically existing MyISAM target.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Known_nontransactional_target_is_rejected_without_an_atomicity_promise(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        var physical = await ReadNativeAsync(store.Observer,
            "SELECT ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='InsertMyIsam';",
            safety.Token);

        var commands = new InsertCommands();
        await using var context = store.CreateContext(commands);

        // Act
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => context
            .Set<InsertSource>()
            .ExecuteInsertFromQueryAsync(context.Set<NonTransactionalTarget>(),
                setters => setters
                    .SetProperty(row => row.Value, row => row.Value),
                safety.Token));

        var actual = await ReadNativeAsync(store.Observer, "SELECT COUNT(*) FROM InsertMyIsam;", safety.Token);

        // Assert
        Assert.Equal(["MyISAM"], physical);
        Assert.Contains("InnoDB", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["0"], actual);
        Assert.Empty(commands.Statements);
    }
}
