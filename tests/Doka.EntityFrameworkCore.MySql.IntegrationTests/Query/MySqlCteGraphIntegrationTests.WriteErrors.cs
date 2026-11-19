namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlCteGraphIntegrationTests
{
    /// <summary>Reports only engines that actually support ordinary native CTE UPDATE and DELETE.</summary>
    public static TheoryData<string> SupportedMutationTargets
    {
        get
        {
            var cases = new TheoryData<string>();

            foreach (var target in SelectedTargets)
            {
                if (IntegrationTestEnvironment
                    .GetServerVersion(Enum.Parse<IntegrationDatabaseTarget>(target))
                    .Profile
                    .Engine
                    .Has(EngineCapability.CteUpdateDelete))
                {
                    cases.Add(target);
                }
            }

            return cases;
        }
    }

    /// <summary>CTE-N21 preserves the native foreign-key failure and atomic statement effect without a hidden retry.</summary>
    [RequiresDatabaseTargetsTheory(IntegrationDatabaseTarget.MySql84,
        IntegrationDatabaseTarget.MySql97,
        IntegrationDatabaseTarget.MariaDb123)]
    [MemberData(nameof(SupportedMutationTargets))]
    public async Task Constraint_failure_preserves_native_error_and_no_partial_cte_update(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        var commands = new GraphCommands();
        await using var context = store.CreateContext(commands);
        var query = context
            .Facets
            .Select(row => row.Id)
            .WithCte(ids => context.Vertices.Where(row => ids.Contains(row.Id)));

        // Act
        var failure = await Record.ExceptionAsync(() => query.ExecuteUpdateAsync(
            setters => setters.SetProperty(row => row.TargetId, 999),
            safety.Token));

        var nativeTotal =
            await store.ReadScalarAsync("SELECT SUM(TargetId) FROM GraphVertices WHERE Id IN (1,2);", safety.Token);

        // Assert
        Assert.Equal(1452,
            Assert.IsType<MySqlException>(failure)
                .Number);

        Assert.Equal(30, Convert.ToInt32(nativeTotal, CultureInfo.InvariantCulture));
        var write = Assert.Single(commands.Writes);
        Assert.Matches(@"\AWITH\s+", write.Sql);
        Assert.Equal(1, CountOrdinaryDefinitions(write.Sql));
        Assert.Equal(safety.Token, write.Token);
        Assert.Empty(commands.Reads);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>CTE-N21 executes each native row trigger exactly once and reports the actual affected count.</summary>
    [RequiresDatabaseTargetsTheory(IntegrationDatabaseTarget.MySql84,
        IntegrationDatabaseTarget.MySql97,
        IntegrationDatabaseTarget.MariaDb123)]
    [MemberData(nameof(SupportedMutationTargets))]
    public async Task Native_trigger_runs_once_per_affected_physical_row(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        await store.ExecuteAsync(
            "CREATE TABLE GraphCteAudit (Id INT NOT NULL); CREATE TRIGGER GraphCteAuditTrigger AFTER UPDATE ON GraphVertices FOR EACH ROW INSERT INTO GraphCteAudit(Id) VALUES (NEW.Id);",
            safety.Token);

        var commands = new GraphCommands();
        await using var context = store.CreateContext(commands);
        var query = context
            .Facets
            .Select(row => row.Id)
            .WithCte(ids => context.Vertices.Where(row => ids.Contains(row.Id)));

        // Act
        var affected =
            await query.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Name, row => row.Name + "!"),
                safety.Token);

        var audit = await store.ReadScalarAsync("SELECT GROUP_CONCAT(Id ORDER BY Id) FROM GraphCteAudit;",
            safety.Token);

        var names = await store.ReadScalarAsync(
            "SELECT GROUP_CONCAT(Name ORDER BY Id) FROM GraphVertices WHERE Id IN (1,2);",
            safety.Token);

        // Assert
        Assert.Equal(2, affected);
        Assert.Equal("1,2", audit);
        Assert.Equal("one!,two!", names);
        Assert.Single(commands.Writes);
        Assert.Empty(commands.Reads);
    }

    /// <summary>CTE-N21 does not turn a failure after native completion into a success, replay or invented rollback.</summary>
    [RequiresDatabaseTargetsTheory(IntegrationDatabaseTarget.MySql84,
        IntegrationDatabaseTarget.MySql97,
        IntegrationDatabaseTarget.MariaDb123)]
    [MemberData(nameof(SupportedMutationTargets))]
    public async Task Failure_after_native_completion_is_preserved_without_replay_or_rollback_claim(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        var expected = new IOException("Fixture-owned failure after the native UPDATE completed.");
        var commands = new GraphCommands { FailureAfterWrite = expected };
        await using var context = store.CreateContext(commands);
        var query = context
            .Facets
            .Select(row => row.Id)
            .WithCte(ids => context.Vertices.Where(row => ids.Contains(row.Id)));

        // Act
        var failure = await Record.ExceptionAsync(() => query.ExecuteUpdateAsync(
            setters => setters.SetProperty(row => row.Name, row => row.Name + "!"),
            safety.Token));

        var names = await store.ReadScalarAsync(
            "SELECT GROUP_CONCAT(Name ORDER BY Id) FROM GraphVertices WHERE Id IN (1,2);",
            safety.Token);

        // Assert
        Assert.Same(expected, failure);
        Assert.Equal("one!,two!", names);
        Assert.Single(commands.Writes);
        Assert.Empty(commands.Reads);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }
}
