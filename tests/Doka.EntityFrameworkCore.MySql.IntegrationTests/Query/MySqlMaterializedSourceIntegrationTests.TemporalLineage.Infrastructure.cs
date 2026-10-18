namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlMaterializedSourceIntegrationTests
{
    private const string TemporalParentTable = "IntBoundTemporalParents";
    private const string TemporalParentHistoryTable = "IntBoundTemporalParentsHistory";
    private const string TemporalChildTable = "IntBoundTemporalChildren";
    private const string TemporalChildHistoryTable = "IntBoundTemporalChildrenHistory";
    private const string TemporalStartColumn = "ValidFrom";
    private const string TemporalEndColumn = "ValidTo";

    private static async Task<TemporalLineageFixture> CreateTemporalLineageFixtureAsync(
        IntegrationDatabaseTarget target
    )
    {
        var probe = new BindingCommandProbe();
        var options = IntegrationTestDbContextOptions.Create<TemporalLineageContext>()
            .UseMySql(
                IntegrationTestEnvironment.GetConnectionString(target),
                IntegrationTestEnvironment.GetServerVersion(target))
            .AddInterceptors(probe)
            .Options;

        var context = new TemporalLineageContext(options);

        try
        {
            await DropTemporalLineageTablesAsync(context);
            await CreateTemporalLineageTablesAsync(context);
            var historicalNext = new BoundHistoricalChild { Id = 3, Name = "historical-next" };
            var currentNext = new BoundHistoricalChild { Id = 4, Name = "current-next" };
            var historicalChild = new BoundHistoricalChild
            {
                Id = 1,
                Name = "historical-child",
                Next = historicalNext,
            };

            var currentChild = new BoundHistoricalChild { Id = 2, Name = "current-parent-target" };
            var parent = new BoundHistoricalParent { Id = 1, Child = historicalChild };
            context.AddRange(parent, currentChild, currentNext);
            await context.SaveChangesAsync(CancellationToken.None);
            parent.Child = currentChild;
            historicalChild.Name = "changed-child";
            historicalChild.Next = currentNext;
            historicalNext.Name = "changed-next";
            await context.SaveChangesAsync(CancellationToken.None);
            context.ChangeTracker.Clear();

            var parentPeriod = await context.Parents.TemporalAll()
                .Where(row => row.ChildId == 1)
                .Select(row => new HistoricalPeriod(
                    EF.Property<DateTime>(row, TemporalStartColumn),
                    EF.Property<DateTime>(row, TemporalEndColumn)))
                .SingleAsync(CancellationToken.None);

            var childPeriod = await context.Children.TemporalAll()
                .Where(row => row.Name == "historical-child")
                .Select(row => new HistoricalPeriod(
                    EF.Property<DateTime>(row, TemporalStartColumn),
                    EF.Property<DateTime>(row, TemporalEndColumn)))
                .SingleAsync(CancellationToken.None);

            var nextPeriod = await context.Children.TemporalAll()
                .Where(row => row.Name == "historical-next")
                .Select(row => new HistoricalPeriod(
                    EF.Property<DateTime>(row, TemporalStartColumn),
                    EF.Property<DateTime>(row, TemporalEndColumn)))
                .SingleAsync(CancellationToken.None);

            // WHY: Use the recorded common historical interval instead of a sleep or
            // the client clock, so both native and emulated temporal tables are stable.
            var from = Math.Max(parentPeriod.From.Ticks, Math.Max(childPeriod.From.Ticks, nextPeriod.From.Ticks));
            var to = Math.Min(parentPeriod.To.Ticks, Math.Min(childPeriod.To.Ticks, nextPeriod.To.Ticks));
            Assert.True(from < to, "The seeded historical versions must have a common nonempty interval.");
            var instant = new DateTime(from + ((to - from) / 2), DateTimeKind.Utc);
            probe.Reset();

            return new TemporalLineageFixture(context, probe, instant);
        }
        catch
        {
            await context.DisposeAsync();

            throw;
        }
    }

    private static async Task CreateTemporalLineageTablesAsync(
        TemporalLineageContext context
    )
    {
        var model = context.GetService<IDesignTimeModel>().Model;
        var operations = context.GetService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel());
        var generator = context.GetService<IMigrationsSqlGenerator>();

        // WHY: Keep provider-owned temporal trigger and native-versioning command
        // boundaries intact, as in the existing temporal integration fixtures.
        foreach (var command in generator.Generate(operations, model))
        {
            await context.Database.ExecuteSqlRawAsync(command.CommandText, CancellationToken.None);
        }
    }

    private static Task<int> DropTemporalLineageTablesAsync(
        TemporalLineageContext context
    ) => context.Database.ExecuteSqlRawAsync(
        $"""
         DROP TABLE IF EXISTS `{TemporalParentTable}`;
         DROP TABLE IF EXISTS `{TemporalChildTable}`;
         DROP TABLE IF EXISTS `{TemporalParentHistoryTable}`, `{TemporalChildHistoryTable}`;
         """,
        CancellationToken.None);

    private sealed class TemporalLineageFixture(
        TemporalLineageContext context,
        BindingCommandProbe probe,
        DateTime instant
    ) : IAsyncDisposable
    {
        public TemporalLineageContext Context { get; } = context;

        public BindingCommandProbe Probe { get; } = probe;

        public DateTime Instant { get; } = instant;

        public async ValueTask DisposeAsync()
        {
            try
            {
                await DropTemporalLineageTablesAsync(Context);
            }
            finally
            {
                await Context.DisposeAsync();
            }
        }
    }

    private sealed class TemporalLineageContext(
        DbContextOptions<TemporalLineageContext> options
    ) : DbContext(options)
    {
        public DbSet<BoundHistoricalParent> Parents => Set<BoundHistoricalParent>();

        public DbSet<BoundHistoricalChild> Children => Set<BoundHistoricalChild>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<BoundHistoricalParent>(entity =>
            {
                entity.ToTable(TemporalParentTable, table => table.IsTemporal(temporal =>
                {
                    temporal.UseHistoryTable(TemporalParentHistoryTable);
                    temporal.HasPeriodStart(TemporalStartColumn);
                    temporal.HasPeriodEnd(TemporalEndColumn);
                }));
                entity.Property(row => row.Id).ValueGeneratedNever();
                entity.HasOne(row => row.Child).WithMany().HasForeignKey(row => row.ChildId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
            modelBuilder.Entity<BoundHistoricalChild>(entity =>
            {
                entity.ToTable(TemporalChildTable, table => table.IsTemporal(temporal =>
                {
                    temporal.UseHistoryTable(TemporalChildHistoryTable);
                    temporal.HasPeriodStart(TemporalStartColumn);
                    temporal.HasPeriodEnd(TemporalEndColumn);
                }));
                entity.Property(row => row.Id).ValueGeneratedNever();
                entity.Property(row => row.Name).HasMaxLength(64);
                entity.HasOne(row => row.Next).WithMany().HasForeignKey(row => row.NextId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }

    private sealed record HistoricalPeriod(
        DateTime From,
        DateTime To
    );

    private sealed class BoundHistoricalParent
    {
        public int Id { get; set; }

        public int ChildId { get; set; }

        public BoundHistoricalChild Child { get; set; } = null!;
    }

    private sealed class BoundHistoricalChild
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public int? NextId { get; set; }

        public BoundHistoricalChild? Next { get; set; }
    }
}
