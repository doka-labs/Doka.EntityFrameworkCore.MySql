namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlCteGraphIntegrationTests
{
    /// <summary>Reports colliding physical table names independently for all three statement-local source kinds.</summary>
    public static IEnumerable<TheoryDataRow<string, string>> PhysicalAliasCases => SelectedTargets.SelectMany(target
        => new[]
        {
            new TheoryDataRow<string, string>(target.Data, "__doka_cte_0"),
            new(target.Data, "__doka_recursive_0"),
            new(target.Data, "__doka_source_0"),
        });

    /// <summary>CTE-N04 resolves typed source collisions without capturing the physical source or another binding.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(PhysicalAliasCases))]
    public async Task Physical_names_colliding_with_generated_source_names_remain_unambiguous(
        string target,
        string physicalName
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        var commands = new GraphCommands();

        // WHY: Each physical name changes the model. An isolated provider graph avoids
        // incorrectly reusing this context type's first table-name model cache entry.
        var options = IntegrationTestDbContextOptions
            .Create<AliasContext>()
            .UseMySql(store.ConnectionString, store.Version)
            .EnableServiceProviderCaching(false)
            .AddInterceptors(commands)
            .Options;

        await using var context = new AliasContext(options, physicalName);
        await store.ExecuteAsync(context.Database.GenerateCreateScript(), safety.Token);
        await store.ExecuteAsync("INSERT INTO `" + physicalName + "` (`Id`) VALUES (7);", safety.Token);
        var source = context
            .Set<AliasRow>()
            .Select(row => row.Id);

        var query = physicalName switch
        {
            "__doka_recursive_0" => source.WithRecursiveCte(frontier => frontier, rows => rows, 1),
            "__doka_source_0" => source.WithMaterializedSource(shared => shared.WithCte(rows => rows.Concat(rows))),
            _ => source.WithCte(rows => rows.Concat(rows)),
        };

        // Act
        var values = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([7, 7], values);
        var command = Assert.Single(commands.Reads);
        Assert.Contains("FROM `" + physicalName + "`", command.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`" + physicalName + "` (", command.Sql, StringComparison.Ordinal);
        Assert.Empty(commands.Writes);
    }

    /// <summary>CTE-N04 includes raw SQL's actual physical name when allocating typed CTE aliases.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Raw_sql_source_collision_keeps_its_physical_table_scope(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        var commands = new GraphCommands();
        var options = IntegrationTestDbContextOptions
            .Create<AliasContext>()
            .UseMySql(store.ConnectionString, store.Version)
            .EnableServiceProviderCaching(false)
            .AddInterceptors(commands)
            .Options;

        await using var context = new AliasContext(options, "__doka_cte_0");
        await store.ExecuteAsync(context.Database.GenerateCreateScript(), safety.Token);
        await store.ExecuteAsync("INSERT INTO `__doka_cte_0` (`Id`) VALUES (7);", safety.Token);
        var query = context
            .Set<AliasRow>()
            .FromSqlRaw("SELECT Id FROM `__doka_cte_0`")
            .Select(row => row.Id)
            .WithCte(rows => rows.Concat(rows));

        // Act
        var values = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([7, 7], values);
        var command = Assert.Single(commands.Reads);
        Assert.Contains("SELECT Id FROM `__doka_cte_0`", command.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`__doka_cte_0` (", command.Sql, StringComparison.Ordinal);
    }

    /// <summary>Distinct columns sharing proved table inheritance retain valid native copying without guessing a default collation.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Same_table_text_columns_with_unconfigured_defaults_remain_compatible(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        var commands = new GraphCommands();
        var options = IntegrationTestDbContextOptions
            .Create<AliasContext>()
            .UseMySql(store.ConnectionString, store.Version)
            .EnableServiceProviderCaching(false)
            .AddInterceptors(commands)
            .Options;

        await using var context = new AliasContext(options, "GraphUnknownText");
        await store.ExecuteAsync(context.Database.GenerateCreateScript(), safety.Token);
        await store.ExecuteAsync("INSERT INTO GraphUnknownText(Id,First,Second) VALUES(7,'first','second');",
            safety.Token);

        var query = context
            .Set<AliasRow>()
            .Select(row => row.First)
            .WithRecursiveCte(
                frontier => frontier.SelectMany(_ => context
                    .Set<AliasRow>()
                    .Select(row => row.Second)),
                rows => rows,
                1);

        // Act
        var values = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal(["first", "second"], values.Order(StringComparer.Ordinal));
        Assert.Single(commands.Reads);
        Assert.Empty(commands.Writes);
    }

    private sealed class AliasContext : DbContext
    {
        private readonly string _tableName;

        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public AliasContext(
            DbContextOptions<AliasContext> options,
            string tableName
        ) : base(options)
        {
            _tableName = tableName;
        }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<AliasRow>(entity =>
            {
                entity.ToTable(_tableName);
                entity
                    .Property(row => row.Id)
                    .ValueGeneratedNever();

                entity
                    .Property(row => row.First)
                    .HasMaxLength(80);

                entity
                    .Property(row => row.Second)
                    .HasMaxLength(80);
            });
        }
    }

    private sealed class AliasRow
    {
        public int Id { get; set; }
        public string? First { get; set; }
        public string? Second { get; set; }
    }
}
