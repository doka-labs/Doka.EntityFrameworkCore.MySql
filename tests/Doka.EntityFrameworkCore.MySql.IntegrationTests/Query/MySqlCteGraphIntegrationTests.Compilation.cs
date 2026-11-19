namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlCteGraphIntegrationTests
{
    /// <summary>CTE-P14 rebinding also covers nonempty recursive anchors, current tenant state and changing compiled depth.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Compiled_nonempty_recursion_rebinds_anchor_context_and_depth_parameters(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        var commands = new GraphCommands();
        await using var first = store.CreateContext(commands);
        await using var second = store.CreateContext(commands);
        second.TenantId = 2;
        var compiled = EF.CompileAsyncQuery((GraphContext database, int start, int depth) => database
            .Vertices
            .Where(row => row.Id == start)
            .Select(row => row.Id)
            .WithRecursiveCte(frontier => frontier.Select(id => id + 1), rows => rows.OrderBy(id => id), depth));

        // Act
        var firstValues = await ReadCompiledAsync(compiled(first, 1, 1), safety.Token);
        var secondValues = await ReadCompiledAsync(compiled(second, 3, 2), safety.Token);

        // Assert
        Assert.Equal([1, 2], firstValues);
        Assert.Equal([3, 4, 5], secondValues);
        Assert.Equal(2, commands.Reads.Count);
        Assert.All(commands.Reads, command => Assert.Equal(safety.Token, command.Token));
    }

    /// <summary>CTE-P14/N23 bind both current scalar parameters and the actual context filter for each compiled execution.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Compiled_ordinary_cte_rebinds_each_context_parameter_and_tenant(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        var commands = new GraphCommands();
        await using var first = store.CreateContext(commands);
        await using var second = store.CreateContext(commands);
        second.TenantId = 2;
        var compiled = EF.CompileAsyncQuery((GraphContext database, int minimum) => database
            .Vertices
            .Where(row => row.Id >= minimum)
            .Select(row => row.Id)
            .WithCte(rows => rows.OrderBy(id => id)));

        // Act
        var firstValues = await ReadCompiledAsync(compiled(first, 2), safety.Token);
        var secondValues = await ReadCompiledAsync(compiled(second, 1), safety.Token);

        // Assert
        Assert.Equal([2, 4, 5, 6], firstValues);
        Assert.Equal([3], secondValues);
        Assert.Equal(2, commands.Reads.Count);
        Assert.All(commands.Reads, command => Assert.Equal(safety.Token, command.Token));
    }

    /// <summary>CTE-P27 checks actual context-pool lease reuse with explicit application state and no old binding metadata.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Pooled_context_reuse_keeps_new_parameters_and_ordinary_queries_clean(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        var commands = new GraphCommands();
        await using var pool = store.CreateContextPool(commands);
        var factory = pool.GetRequiredService<IDbContextFactory<GraphContext>>();
        GraphContext first;
        GraphContext reused;
        int[] firstValues;
        int[] secondValues;
        int ordinaryCount;

        // Act
        await using (first = await factory.CreateDbContextAsync(safety.Token))
        {
            first.TenantId = 1;
            firstValues = await first
                .Vertices
                .Where(row => row.Id > 1)
                .Select(row => row.Id)
                .WithCte(rows => rows.OrderBy(id => id))
                .ToArrayAsync(safety.Token);
        }

        await using (reused = await factory.CreateDbContextAsync(safety.Token))
        {
            // WHY: EF resets its own pool state, not arbitrary application tenant properties.
            // Assign the lease's tenant explicitly so this tests CTE isolation rather than inventing that guarantee.
            reused.TenantId = 2;
            secondValues = await reused
                .Vertices
                .Select(row => row.Id)
                .WithCte(rows => rows)
                .ToArrayAsync(safety.Token);

            ordinaryCount = await reused.Vertices.CountAsync(safety.Token);
        }

        // Assert
        Assert.Same(first, reused);
        Assert.Equal([2, 4, 5, 6], firstValues);
        Assert.Equal([3], secondValues);
        Assert.Equal(1, ordinaryCount);
        var statements = commands.Reads.ToArray();
        Assert.Equal(3, statements.Length);
        Assert.DoesNotContain("WITH", statements[2].Sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>CTE-P27 starts independent compilations over one immutable provider graph and checks every complete result.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Parallel_compilations_keep_contexts_filters_and_parameters_isolated(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        var commands = new GraphCommands();

        // Act
        var actual = await Task.WhenAll(
            ReadTenantAsync(store, commands, 1, 1, safety.Token),
            ReadTenantAsync(store, commands, 2, 1, safety.Token),
            ReadTenantAsync(store, commands, 1, 5, safety.Token),
            ReadTenantAsync(store, commands, 2, 4, safety.Token));

        // Assert
        Assert.Equal([1, 2, 4, 5, 6], actual[0]);
        Assert.Equal([3], actual[1]);
        Assert.Equal([5, 6], actual[2]);
        Assert.Empty(actual[3]);
        Assert.Equal(4, commands.Reads.Count);
        Assert.All(commands.Reads, command => Assert.Equal(1, CountOrdinaryDefinitions(command.Sql)));
    }

    /// <summary>CTE-P14/N15 distinguish two supplied finalized models with the same context and entity CLR types.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Supplied_models_keep_their_distinct_physical_entity_lineage(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        await using var original = store.CreateContext();
        var firstModel = original.Model;
        var secondModel = await CreateAlternateModelAsync(store, safety.Token);
        await using var first = store.CreateContext(model: firstModel);
        await using var second = store.CreateContext(model: secondModel);

        // Act
        var firstValues = await first
            .Vertices
            .Select(row => row.Id)
            .WithCte(rows => rows.OrderBy(id => id))
            .ToArrayAsync(safety.Token);

        var secondValues = await second
            .Vertices
            .Select(row => row.Id)
            .WithCte(rows => rows.OrderBy(id => id))
            .ToArrayAsync(safety.Token);

        // Assert
        Assert.NotSame(first.Model, second.Model);
        Assert.Equal([1, 2, 4, 5, 6], firstValues);
        Assert.Equal([101, 102, 104, 105, 106], secondValues);
    }

    /// <summary>CTE-N15/N16 preserve EF's explicit compiled-query model boundary instead of reusing incompatible metadata.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Compiled_cte_rejects_a_different_supplied_model_before_a_data_command(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        await using var original = store.CreateContext();
        var secondModel = await CreateAlternateModelAsync(store, safety.Token);
        var commands = new GraphCommands();
        await using var second = store.CreateContext(commands, secondModel);
        var compiled = EF.CompileAsyncQuery((GraphContext database) => database
            .Vertices
            .Select(row => row.Id)
            .WithCte(rows => rows));

        _ = await ReadCompiledAsync(compiled(original), safety.Token);

        // Act
        var failure = await Record.ExceptionAsync(() => ReadCompiledAsync(compiled(second), safety.Token));

        // Assert
        Assert.Contains("different model",
            Assert.IsType<InvalidOperationException>(failure)
                .Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Empty(commands.Reads);
    }

    /// <summary>CTE-N16 retains the invalid flat-source boundary when the caller explicitly compiles the query.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Compiled_client_source_is_rejected_without_executing_a_partial_query(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        var commands = new GraphCommands();
        await using var context = store.CreateContext(commands);
        var compiled = EF.CompileAsyncQuery((GraphContext database) => database
            .Vertices
            .Select(row => ClientSourceValue(row.Name))
            .WithCte(rows => rows));

        // Act
        var failure = await Record.ExceptionAsync(() => ReadCompiledAsync(compiled(context), safety.Token));

        // Assert
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Empty(commands.Reads);
    }

    /// <summary>Reads one independent context with current filter and scalar parameters.</summary>
    private static async Task<int[]> ReadTenantAsync(
        GraphStore store,
        GraphCommands commands,
        int tenant,
        int minimum,
        CancellationToken cancellationToken
    )
    {
        await using var context = store.CreateContext(commands);
        context.TenantId = tenant;

        return await context
            .Vertices
            .Where(row => row.Id >= minimum)
            .Select(row => row.Id)
            .WithCte(rows => rows.OrderBy(id => id))
            .ToArrayAsync(cancellationToken);
    }

    /// <summary>Builds a distinct finalized model in a separate provider cache and seeds its different physical table.</summary>
    private static async Task<IModel> CreateAlternateModelAsync(
        GraphStore store,
        CancellationToken cancellationToken
    )
    {
        await using var services = new ServiceCollection()
            .AddEntityFrameworkDokaMySql()
            .BuildServiceProvider();

        var options = IntegrationTestDbContextOptions
            .Create<GraphContext>()
            .UseMySql(store.ConnectionString, store.Version)
            .UseInternalServiceProvider(services)
            .Options;

        await using var context = new GraphContext(options) { AlternateVertices = true };
        var model = context.Model;
        await store.ExecuteAsync("CREATE TABLE `GraphVerticesAlternate` LIKE `GraphVertices`; "
            + "INSERT INTO `GraphVerticesAlternate` (`Id`,`TenantId`,`TargetId`,`Name`,`FieldNote`,`ShadowNumber`,`Kind`,`Tag`) "
            + "SELECT `Id`+100,`TenantId`,`TargetId`,`Name`,`FieldNote`,`ShadowNumber`,`Kind`,`Tag` "
            + "FROM `GraphVertices`;",
            cancellationToken);

        return model;
    }

    /// <summary>Provides an intentional client-only source function with no SQL translation.</summary>
    private static string ClientSourceValue(
        string value
    ) => "client:" + value;
}
