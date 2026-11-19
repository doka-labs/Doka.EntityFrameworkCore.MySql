namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlCteGraphIntegrationTests
{
    /// <summary>Reports named-filter opt-outs separately from the fully filtered control.</summary>
    public static TheoryData<string, string> NamedFilterCases
    {
        get
        {
            var cases = new TheoryData<string, string>();

            foreach (var target in SelectedTargets)
            {
                cases.Add(target, "none");
                cases.Add(target, "Tenant");
                cases.Add(target, "Name");
            }

            return cases;
        }
    }

    /// <summary>Separates the named navigated-target opt-out from its filtered control.</summary>
    public static TheoryData<string, bool> NamedTargetFilterCases
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

    /// <summary>CTE-P10/P12/P13 retain same-table owned values and every independently named source filter.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(NamedFilterCases))]
    public async Task Named_source_filters_and_flat_owned_values_survive_typed_binding(
        string target,
        string ignored
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        var commands = new GraphCommands();
        await using var context = store.CreateNamedContext(commands);
        IQueryable<NamedGraphRow> source = context.Rows;

        if (ignored != "none")
        {
            source = source.IgnoreQueryFilters([ignored]);
        }

        var query = source
            .WithCte(rows => rows.Select(row => new
            {
                row.Id,
                row.Detail.Note,
                row.Detail.Number,
            }))
            .OrderBy(row => row.Id);

        int[] expected = ignored switch { "Tenant" => [1, 3, 4, 5, 6], "Name" => [1, 2, 4, 5, 6], _ => [1, 4, 5, 6] };

        // Act
        var actual = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal(expected, actual.Select(row => row.Id));
        Assert.Equal(expected.Select(id => "field-" + id.ToString(CultureInfo.InvariantCulture)),
            actual.Select(row => row.Note));

        Assert.Equal(expected.Select(id => id + 100), actual.Select(row => row.Number));
        Assert.Single(commands.Reads);
        Assert.Empty(commands.Writes);
    }

    /// <summary>CTE-N14 preserves a navigated target's named filter after one different source filter was explicitly disabled.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(NamedTargetFilterCases))]
    public async Task Named_target_filter_is_independent_from_a_disabled_source_filter(
        string target,
        bool ignoreTarget
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        var commands = new GraphCommands();
        await using var context = store.CreateNamedContext(commands);
        var source = context.Rows.IgnoreQueryFilters(["Name"]);

        if (ignoreTarget)
        {
            source = source.IgnoreQueryFilters(["Name", "Visible"]);
        }

        var query = source
            .WithCte(rows => rows.Select(row => new
            {
                row.Id,
                row.Target.Name,
            }))
            .OrderBy(row => row.Id);

        int[] expected = ignoreTarget
            ? [1, 2, 4, 5, 6]
            : [1, 4, 5, 6];

        // Act
        var actual = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal(expected, actual.Select(row => row.Id));
        Assert.Equal(ignoreTarget
                ? 1
                : 0,
            actual.Count(row => row.Name == "hidden"));

        Assert.Single(commands.Reads);
        Assert.Empty(commands.Writes);
    }

    private sealed partial class GraphStore
    {
        /// <summary>Shares only the owned connection/provider graph; a distinct context type keeps named-filter metadata separate.</summary>
        internal NamedGraphContext CreateNamedContext(
            GraphCommands commands
        ) => new(
            IntegrationTestDbContextOptions
                .Create<NamedGraphContext>()
                .UseMySql(ConnectionString, Version)
                .UseInternalServiceProvider(_services)
                .AddInterceptors(commands)
                .Options);
    }

    private sealed class NamedGraphContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public NamedGraphContext(
            DbContextOptions<NamedGraphContext> options
        ) : base(options) { }

        public int TenantId { get; set; } = 1;
        public DbSet<NamedGraphRow> Rows => Set<NamedGraphRow>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.HasCharSet("utf8mb4");
            modelBuilder.UseCollation("utf8mb4_unicode_ci");
            modelBuilder.Entity<NamedGraphRow>(entity =>
            {
                entity.ToTable("GraphVertices");
                entity
                    .Property(row => row.Id)
                    .ValueGeneratedNever();

                entity.HasQueryFilter("Tenant", row => row.TenantId == TenantId);
                entity.HasQueryFilter("Name", row => row.Id != 2);
                entity
                    .HasOne(row => row.Target)
                    .WithMany()
                    .HasForeignKey(row => row.TargetId);

                entity.OwnsOne(row => row.Detail,
                    owned =>
                    {
                        owned
                            .Property(detail => detail.Note)
                            .HasColumnName("FieldNote")
                            .HasMaxLength(80);

                        owned
                            .Property(detail => detail.Number)
                            .HasColumnName("ShadowNumber");
                    });

                entity
                    .Navigation(row => row.Detail)
                    .IsRequired();
            });

            modelBuilder.Entity<NamedGraphTarget>(entity =>
            {
                entity.ToTable("GraphTargets");
                entity
                    .Property(row => row.Name)
                    .HasMaxLength(80);

                entity.HasQueryFilter("Visible", row => row.IsVisible);
            });
        }
    }

    private sealed class NamedGraphRow
    {
        public int Id { get; set; }
        public int TenantId { get; set; }
        public int TargetId { get; set; }
        public NamedGraphTarget Target { get; set; } = null!;
        public NamedGraphDetail Detail { get; set; } = null!;
    }

    private sealed class NamedGraphDetail
    {
        public string Note { get; set; } = string.Empty;
        public int Number { get; set; }
    }

    private sealed class NamedGraphTarget
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsVisible { get; set; }
    }
}
