namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlCteGraphIntegrationTests
{
    /// <summary>Separates both mutation terminals so neither rejection can hide the other.</summary>
    public static IEnumerable<TheoryDataRow<string, bool>> AliasMutationCases => SelectedTargets.SelectMany(target
        => new[] { new TheoryDataRow<string, bool>(target.Data, false), new(target.Data, true) });

    /// <summary>Reports actual profiles that need independent isolation for a self-referencing native write.</summary>
    public static IEnumerable<TheoryDataRow<string>> IsolatedMutationTargets => SupportedMutationTargets.Where(target =>
        IntegrationTestEnvironment
            .GetServerVersion(Enum.Parse<IntegrationDatabaseTarget>(target.Data))
            .Profile
            .GetSupport(ProviderCapability.SelfReferencingMutations)
        == ProviderSupportStatus.Emulated);

    /// <summary>A mapped entity terminal either rewrites to its real physical table or retains the established dialect/self-reference guard.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(AliasMutationCases))]
    public async Task Entity_binding_terminal_uses_a_qualified_physical_target_or_the_existing_guard(
        string target,
        bool update
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
            .WithCte(rows => rows);

        var physicalRewrite = store.Version.Profile.Engine.Has(EngineCapability.CteUpdateDelete)
            && store.Version.Profile.GetSupport(ProviderCapability.SelfReferencingMutations)
            == ProviderSupportStatus.Native;

        var affected = -1;

        // Act
        var failure = await Record.ExceptionAsync(async () =>
        {
            affected = update
                ? await query.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Name, "updated"),
                    safety.Token)
                : await query.ExecuteDeleteAsync(safety.Token);
        });

        var names = await store.ReadScalarAsync("SELECT GROUP_CONCAT(Name ORDER BY Id) FROM GraphVertices;",
            safety.Token);

        // Assert
        if (physicalRewrite)
        {
            Assert.Null(failure);
            Assert.Equal(2, affected);
            Assert.Equal(update
                    ? "updated,updated,three,four,five,six"
                    : "three,four,five,six",
                names);

            var write = Assert.Single(commands.Writes);
            Assert.Matches(update
                    ? @"(?m)^UPDATE `GraphVertices` AS "
                    : @"(?m)^DELETE `[^`]+`\r?\nFROM `GraphVertices` AS ",
                write.Sql);

            Assert.Equal(safety.Token, write.Token);
        }
        else
        {
            Assert.IsType<InvalidOperationException>(failure);
            Assert.Equal(-1, affected);
            Assert.Equal("one,two,three,four,five,six", names);
            Assert.Empty(commands.Writes);
        }

        Assert.Empty(commands.Reads);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>CTE-N18 rejects a genuinely unmapped DTO target, independently of legal entity-to-physical-table rewrites.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(AliasMutationCases))]
    public async Task Unmapped_dto_is_not_invented_as_a_physical_mutation_target(
        string target,
        bool update
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
            .WithCte(ids => ids.Select(id => new UnmappedCteWriteTarget { Value = id }));

        // Act
        var failure = await Record.ExceptionAsync(async () =>
        {
            _ = update
                ? await query.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, -1), safety.Token)
                : await query.ExecuteDeleteAsync(safety.Token);
        });

        var count = await store.ReadScalarAsync("SELECT COUNT(*) FROM GraphFacets;", safety.Token);

        // Assert
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Equal(2, Convert.ToInt32(count, CultureInfo.InvariantCulture));
        Assert.Empty(commands.Writes);
        Assert.Empty(commands.Reads);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>CTE-N20 preserves the existing materialized-source write boundary even when nested inside an ordinary CTE.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(AliasMutationCases))]
    public async Task Nested_materialized_source_does_not_acquire_mutation_semantics(
        string target,
        bool update
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
            .WithMaterializedSource(shared =>
                shared.WithCte(ids => context.Vertices.Where(row => ids.Contains(row.Id))));

        // Act
        var failure = await Record.ExceptionAsync(async () =>
        {
            _ = update
                ? await query.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Name, "must-not-write"),
                    safety.Token)
                : await query.ExecuteDeleteAsync(safety.Token);
        });

        var names = await store.ReadScalarAsync(
            "SELECT GROUP_CONCAT(Name ORDER BY Id) FROM GraphVertices WHERE Id IN (1,2);",
            safety.Token);

        // Assert
        Assert.Contains("read queries only",
            Assert.IsType<InvalidOperationException>(failure)
                .Message,
            StringComparison.Ordinal);

        Assert.Equal("one,two", names);
        Assert.Empty(commands.Writes);
        Assert.Empty(commands.Reads);
    }

    /// <summary>CTE-N19 rejects an unsafe same-table source before relying on syntactic WITH as target isolation.</summary>
    [RequiresDatabaseTargetsTheory([IntegrationDatabaseTarget.MySql84, IntegrationDatabaseTarget.MySql97])]
    [MemberData(nameof(IsolatedMutationTargets))]
    public async Task Unsafe_self_referencing_cte_write_is_rejected_before_native_execution(
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
            .WithCte(ids =>
                context.Vertices.Where(row => ids.Contains(row.Id)));

        // Act
        var failure = await Record.ExceptionAsync(() => query.ExecuteUpdateAsync(
            setters => setters.SetProperty(row => row.Name, "must-not-write"),
            safety.Token));

        var names = await store.ReadScalarAsync(
            "SELECT GROUP_CONCAT(Name ORDER BY Id) FROM GraphVertices WHERE Id IN (1,2);",
            safety.Token);

        // Assert
        Assert.Contains("Self-referencing",
            Assert.IsType<InvalidOperationException>(failure)
                .Message,
            StringComparison.Ordinal);

        Assert.Equal("one,two", names);
        Assert.Empty(commands.Writes);
        Assert.Empty(commands.Reads);
    }

    /// <summary>Provides a scalar DTO with no entity metadata from which EF could infer a physical update target.</summary>
    private sealed class UnmappedCteWriteTarget
    {
        /// <summary>Gets or sets the projected value without an EF property mapping.</summary>
        public int Value { get; set; }
    }
}
