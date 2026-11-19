namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlInsertSelectIntegrationTests
{
    private static int s_clientEvaluations;

    /// <summary>Reports independent invalid assignments and source shapes without running a write.</summary>
    public static IEnumerable<TheoryDataRow<string, string>> RejectionCases => SelectedTargets.SelectMany(target
        => new[]
        {
            new TheoryDataRow<string, string>(target.Data, "duplicate"),
            new TheoryDataRow<string, string>(target.Data, "unmapped"),
            new TheoryDataRow<string, string>(target.Data, "computed"),
            new TheoryDataRow<string, string>(target.Data, "required"),
            new TheoryDataRow<string, string>(target.Data, "client-method"),
            new TheoryDataRow<string, string>(target.Data, "split"),
            new TheoryDataRow<string, string>(target.Data, "collection-include"),
            new TheoryDataRow<string, string>(target.Data, "materialized"),
            new TheoryDataRow<string, string>(target.Data, "nested-materialized"),
            new TheoryDataRow<string, string>(target.Data, "locking"),
            new TheoryDataRow<string, string>(target.Data, "self-subquery"),
        });

    /// <summary>Invalid mappings and unsupported source lineage fail before any data command or client callback.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(RejectionCases))]
    public async Task Invalid_assignment_or_source_is_rejected_before_any_write(
        string target,
        string invalid
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        var commands = new InsertCommands();
        await using var context = store.CreateContext(commands);
        await using var transaction = await context.Database.BeginTransactionAsync(safety.Token);
        var source = context.Set<InsertSource>();
        var baselineCallbacks = Volatile.Read(ref s_clientEvaluations);
        var query = invalid switch
        {
            "split" => source.AsSplitQuery(),
            "collection-include" => source.Include(row => row.Parent.Children),
            "materialized" => source.WithMaterializedSource(rows => rows),
            "nested-materialized" => source.Where(row => source
                .WithMaterializedSource(rows => rows.Select(inner => inner.Id))
                .Contains(row.Id)),
            "locking" => source.ForUpdate(),
            "self-subquery" => source.Where(row => context
                .Set<InsertTarget>()
                .Any(inner => inner.Value == row.Value)),
            _ => source,
        };

        // Act
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => query.ExecuteInsertFromQueryAsync(
            context.Set<InsertTarget>(),
            setters =>
            {
                setters.SetProperty(row => row.Code, row => row.Label);

                if (invalid != "required")
                {
                    if (invalid == "client-method")
                    {
                        setters.SetProperty(row => row.Value, row => ClientOnly(row.Value));
                    }
                    else
                    {
                        setters.SetProperty(row => row.Value, row => row.Value);
                    }
                }

                if (invalid == "duplicate")
                {
                    setters.SetProperty(row => row.Code, row => "second");
                }
                else if (invalid == "unmapped")
                {
                    setters.SetProperty(row => row.Unmapped, row => row.Label);
                }
                else if (invalid == "computed")
                {
                    setters.SetProperty(row => row.Computed, row => row.Value);
                }
            },
            safety.Token));

        var actual = await ReadNativeAsync(store.Observer, $"SELECT COUNT(*) FROM `{TargetTable}`;", safety.Token);

        // Assert
        var reason = invalid switch
        {
            "duplicate" => "more than once",
            "unmapped" => "not a mapped scalar property",
            "computed" => "generated or read-only",
            "required" => "requires a value",
            "client-method" => "translat",
            "split" => "split-query",
            "collection-include" => "collection Include",
            "materialized" or "nested-materialized" => "read-only materialized source",
            "locking" => "locking-read source",
            "self-subquery" => "nested self-subquery",
            _ => throw new ArgumentOutOfRangeException(nameof(invalid)),
        };

        Assert.Contains(reason, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["0"], actual);
        Assert.Empty(commands.Statements);
        Assert.Equal(0, commands.Readers);
        Assert.Equal(baselineCallbacks, Volatile.Read(ref s_clientEvaluations));
    }

    /// <summary>Cross-context sources cannot write even when the two contexts use the same connection string and model graph.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Different_context_is_rejected_before_command_execution(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        var commands = new InsertCommands();
        await using var sourceContext = store.CreateContext(commands);
        await using var targetContext = store.CreateContext(commands);

        // Act
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => sourceContext
            .Set<InsertSource>()
            .ExecuteInsertFromQueryAsync(targetContext.Set<InsertTarget>(),
                setters => setters
                    .SetProperty(row => row.Value, row => row.Value)
                    .SetProperty(row => row.Code, row => row.Label),
                safety.Token));

        var persisted = await ReadNativeAsync(store.Observer, $"SELECT COUNT(*) FROM `{TargetTable}`;", safety.Token);

        // Assert
        Assert.Contains("same", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["0"], persisted);
        Assert.Empty(commands.Statements);
        Assert.Equal(0, commands.Readers);
    }

    /// <summary>A client-only GUID generator is neither run once nor replaced with an invented SQL UUID default.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Missing_client_only_guid_generator_is_rejected_before_write(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        var commands = new InsertCommands();
        await using var context = store.CreateContext(commands);

        // Act
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => context
            .Set<InsertSource>()
            .ExecuteInsertFromQueryAsync(context.Set<ClientGuidTarget>(),
                setters => setters
                    .SetProperty(row => row.Value, row => row.Value),
                safety.Token));

        var persisted = await ReadNativeAsync(store.Observer, "SELECT COUNT(*) FROM InsertClientGuid;", safety.Token);

        // Assert
        Assert.Contains("requires a value for physical column", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["0"], persisted);
        Assert.Empty(commands.Statements);
        Assert.Equal(0, commands.Readers);
    }

    /// <summary>Has no SQL implementation; the callback counter detects accidental local evaluation.</summary>
    private static int ClientOnly(
        int value
    )
    {
        Interlocked.Increment(ref s_clientEvaluations);

        return value;
    }
}
