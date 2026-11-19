namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlCteGraphIntegrationTests
{
    /// <summary>Creates actual history with recorded overlapping periods, never a client-clock or sleep assumption.</summary>
    private static async Task<DateTime> SeedHistoricalGraphAsync(
        GraphTemporalContext context,
        CancellationToken cancellationToken
    )
    {
        var model = context.GetService<IDesignTimeModel>()
            .Model;

        var operations = context
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(null, model.GetRelationalModel());

        var generator = context.GetService<IMigrationsSqlGenerator>();

        // WHY: Native versioning and emulated trigger commands retain their actual provider-owned boundaries.
        foreach (var command in generator.Generate(operations, model))
        {
            await context.Database.ExecuteSqlRawAsync(command.CommandText, cancellationToken);
        }

        var oldNext = new GraphHistoryTarget
        {
            Id = 3,
            Name = "old-next",
        };

        var currentNext = new GraphHistoryTarget
        {
            Id = 4,
            Name = "current-next",
        };

        var oldTarget = new GraphHistoryTarget
        {
            Id = 1,
            Name = "old",
            Next = oldNext,
        };

        var currentTarget = new GraphHistoryTarget
        {
            Id = 2,
            Name = "current",
        };

        var node = new GraphHistoryNode
        {
            Id = 1,
            Target = oldTarget,
        };

        context.Targets.AddRange(oldTarget, currentTarget, oldNext, currentNext);
        context.Nodes.Add(node);
        await context.SaveChangesAsync(cancellationToken);
        node.Target = currentTarget;
        oldTarget.Name = "changed";
        oldTarget.Next = currentNext;
        oldNext.Name = "changed-next";
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();

        var nodePeriod = await context
            .Nodes
            .TemporalAll()
            .Where(row => row.TargetId == 1)
            .Select(row => new GraphHistoryPeriod(EF.Property<DateTime>(row, "ValidFrom"),
                EF.Property<DateTime>(row, "ValidTo")))
            .SingleAsync(cancellationToken);

        var targetPeriod = await context
            .Targets
            .TemporalAll()
            .Where(row => row.Name == "old")
            .Select(row => new GraphHistoryPeriod(EF.Property<DateTime>(row, "ValidFrom"),
                EF.Property<DateTime>(row, "ValidTo")))
            .SingleAsync(cancellationToken);

        var nextPeriod = await context
            .Targets
            .TemporalAll()
            .Where(row => row.Name == "old-next")
            .Select(row => new GraphHistoryPeriod(EF.Property<DateTime>(row, "ValidFrom"),
                EF.Property<DateTime>(row, "ValidTo")))
            .SingleAsync(cancellationToken);

        var from = Math.Max(nodePeriod.From.Ticks, Math.Max(targetPeriod.From.Ticks, nextPeriod.From.Ticks));
        var to = Math.Min(nodePeriod.To.Ticks, Math.Min(targetPeriod.To.Ticks, nextPeriod.To.Ticks));

        if (from >= to)
        {
            throw new InvalidOperationException(
                "Seeded native historical versions must have a common nonempty interval.");
        }

        return new DateTime(from + (to - from) / 2, DateTimeKind.Utc);
    }

    private sealed partial class GraphStore
    {
        /// <summary>Reuses only this store's fixed provider graph; temporal metadata has a distinct context model key.</summary>
        internal GraphTemporalContext CreateTemporalContext(
            GraphCommands commands
        ) => new(IntegrationTestDbContextOptions
            .Create<GraphTemporalContext>()
            .UseMySql(ConnectionString, Version)
            .UseInternalServiceProvider(_services)
            .AddInterceptors(commands)
            .Options);
    }

    /// <summary>Contains the real provider-native or emulated system-versioned table contract.</summary>
    private sealed class GraphTemporalContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public GraphTemporalContext(
            DbContextOptions<GraphTemporalContext> options
        ) : base(options) { }

        /// <summary>Gets system-versioned roots.</summary>
        public DbSet<GraphHistoryNode> Nodes => Set<GraphHistoryNode>();

        /// <summary>Gets separately stored system-versioned navigation targets.</summary>
        public DbSet<GraphHistoryTarget> Targets => Set<GraphHistoryTarget>();

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.HasCharSet("utf8mb4");
            modelBuilder.Entity<GraphHistoryNode>(entity =>
            {
                entity.ToTable("GraphHistoryNodes",
                    table => table.IsTemporal(temporal =>
                    {
                        temporal.UseHistoryTable("GraphHistoryNodesHistory");
                        temporal.HasPeriodStart("ValidFrom");
                        temporal.HasPeriodEnd("ValidTo");
                    }));

                entity
                    .Property(row => row.Id)
                    .ValueGeneratedNever();

                entity
                    .HasOne(row => row.Target)
                    .WithMany()
                    .HasForeignKey(row => row.TargetId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<GraphHistoryTarget>(entity =>
            {
                entity.ToTable("GraphHistoryTargets",
                    table => table.IsTemporal(temporal =>
                    {
                        temporal.UseHistoryTable("GraphHistoryTargetsHistory");
                        temporal.HasPeriodStart("ValidFrom");
                        temporal.HasPeriodEnd("ValidTo");
                    }));

                entity
                    .Property(row => row.Id)
                    .ValueGeneratedNever();

                entity
                    .Property(row => row.Name)
                    .HasMaxLength(80);

                entity
                    .HasOne(row => row.Next)
                    .WithMany()
                    .HasForeignKey(row => row.NextId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }

    /// <summary>Captures the native recorded validity interval.</summary>
    private sealed record GraphHistoryPeriod(
        DateTime From,
        DateTime To
    );

    private sealed class GraphHistoryNode
    {
        /// <summary>Gets or sets the physical temporal key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the independently versioned target key.</summary>
        public int TargetId { get; set; }

        /// <summary>Gets or sets the reference whose historical lineage is tested.</summary>
        public GraphHistoryTarget Target { get; set; } = null!;
    }

    private sealed class GraphHistoryTarget
    {
        /// <summary>Gets or sets the temporal target identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the temporal scalar payload.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the optional next target identity.</summary>
        public int? NextId { get; set; }

        /// <summary>Gets or sets a second navigation with its own historical state.</summary>
        public GraphHistoryTarget? Next { get; set; }
    }
}
