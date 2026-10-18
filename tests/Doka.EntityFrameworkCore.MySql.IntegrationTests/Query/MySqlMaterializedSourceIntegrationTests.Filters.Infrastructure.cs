namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlMaterializedSourceIntegrationTests
{
    private const string FilterSourceATable = "IntBoundFilterA";
    private const string FilterSourceBTable = "IntBoundFilterB";
    private const string FilterSourceCTable = "IntBoundFilterC";

    private static async Task<FilterFixture> CreateFilterFixtureAsync(
        IntegrationDatabaseTarget target
    )
    {
        var probe = new BindingCommandProbe();
        var options = IntegrationTestDbContextOptions.Create<FilterContext>()
            .UseMySql(
                IntegrationTestEnvironment.GetConnectionString(target),
                IntegrationTestEnvironment.GetServerVersion(target))
            .AddInterceptors(probe)
            .Options;

        var context = new FilterContext(options);

        try
        {
            await DropFilterTablesAsync(context);
            await context.Database.ExecuteSqlRawAsync(context.Database.GenerateCreateScript(), CancellationToken.None);

            for (var id = 1; id <= 6; id++)
            {
                context.Add(new FilterSourceA { Id = id });
                context.Add(new FilterSourceB { Id = id });
                context.Add(new FilterSourceC { Id = id });
            }

            await context.SaveChangesAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            probe.Reset();

            return new FilterFixture(context, probe);
        }
        catch
        {
            await context.DisposeAsync();

            throw;
        }
    }

    private static Task<int> DropFilterTablesAsync(
        FilterContext context
    ) => context.Database.ExecuteSqlRawAsync(
        $"DROP TABLE IF EXISTS `{FilterSourceATable}`, `{FilterSourceBTable}`, `{FilterSourceCTable}`;",
        CancellationToken.None);

    private sealed class FilterFixture(
        FilterContext context,
        BindingCommandProbe probe
    ) : IAsyncDisposable
    {
        public FilterContext Context { get; } = context;

        public BindingCommandProbe Probe { get; } = probe;

        public async ValueTask DisposeAsync()
        {
            try
            {
                await DropFilterTablesAsync(Context);
            }
            finally
            {
                await Context.DisposeAsync();
            }
        }
    }

    private sealed class FilterContext(
        DbContextOptions<FilterContext> options
    ) : DbContext(options)
    {
        public int MinimumAValue { get; set; } = 2;

        public int MinimumBValue { get; set; } = 4;

        public int MinimumCValue { get; set; } = 6;

        public DbSet<FilterSourceA> SourceA => Set<FilterSourceA>();

        public DbSet<FilterSourceB> SourceB => Set<FilterSourceB>();

        public DbSet<FilterSourceC> SourceC => Set<FilterSourceC>();

        // WHY: Separate context method calls exercise EF's generated filter parameter
        // names; separate navigation visitors would reuse the same parameter stem.
        private int ReadMinimumA() => MinimumAValue;

        private int ReadMinimumB() => MinimumBValue;

        private int ReadMinimumC() => MinimumCValue;

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<FilterSourceA>(entity =>
            {
                entity.ToTable(FilterSourceATable);
                entity.Property(row => row.Id).ValueGeneratedNever();
                entity.HasQueryFilter(row => row.Id >= ReadMinimumA());
            });
            modelBuilder.Entity<FilterSourceB>(entity =>
            {
                entity.ToTable(FilterSourceBTable);
                entity.Property(row => row.Id).ValueGeneratedNever();
                entity.HasQueryFilter(row => row.Id >= ReadMinimumB());
            });
            modelBuilder.Entity<FilterSourceC>(entity =>
            {
                entity.ToTable(FilterSourceCTable);
                entity.Property(row => row.Id).ValueGeneratedNever();
                entity.HasQueryFilter(row => row.Id >= ReadMinimumC());
            });
        }
    }

    private sealed class FilterSourceA
    {
        public int Id { get; set; }
    }

    private sealed class FilterSourceB
    {
        public int Id { get; set; }
    }

    private sealed class FilterSourceC
    {
        public int Id { get; set; }
    }
}
