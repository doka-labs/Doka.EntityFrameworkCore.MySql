namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlMaterializedSourceIntegrationTests
{
    private const string NamedFirstTable = "IntBoundNamedFirst";
    private const string NamedSecondTable = "IntBoundNamedSecond";
    private const string NamedTargetTable = "IntBoundNamedTargets";

    private static async Task<NamedLineageFixture> CreateNamedLineageFixtureAsync(
        IntegrationDatabaseTarget target
    )
    {
        var probe = new BindingCommandProbe();
        var options = IntegrationTestDbContextOptions.Create<NamedLineageContext>()
            .UseMySql(
                IntegrationTestEnvironment.GetConnectionString(target),
                IntegrationTestEnvironment.GetServerVersion(target))
            .AddInterceptors(probe)
            .Options;

        var context = new NamedLineageContext(options);

        try
        {
            await DropNamedLineageTablesAsync(context);
            await context.Database.ExecuteSqlRawAsync(context.Database.GenerateCreateScript(), CancellationToken.None);
            context.Targets.AddRange(
                new NamedLineageTarget { Id = 11, Name = "first-target" },
                new NamedLineageTarget { Id = 22, Name = "second-target" });
            // WHY: Equal keys in both named mappings make a lost mapping identity
            // return the other target instead of passing because the key is unique.
            context.Set<NamedLineageNode>("First").Add(new NamedLineageNode { Id = 1, TargetId = 11 });
            context.Set<NamedLineageNode>("Second").Add(new NamedLineageNode { Id = 1, TargetId = 22 });
            await context.SaveChangesAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            probe.Reset();

            return new NamedLineageFixture(context, probe);
        }
        catch
        {
            await context.DisposeAsync();

            throw;
        }
    }

    private static Task<int> DropNamedLineageTablesAsync(
        NamedLineageContext context
    ) => context.Database.ExecuteSqlRawAsync(
        $"DROP TABLE IF EXISTS `{NamedFirstTable}`, `{NamedSecondTable}`; DROP TABLE IF EXISTS `{NamedTargetTable}`;",
        CancellationToken.None);

    private sealed class NamedLineageFixture(
        NamedLineageContext context,
        BindingCommandProbe probe
    ) : IAsyncDisposable
    {
        public NamedLineageContext Context { get; } = context;

        public BindingCommandProbe Probe { get; } = probe;

        public async ValueTask DisposeAsync()
        {
            try
            {
                await DropNamedLineageTablesAsync(Context);
            }
            finally
            {
                await Context.DisposeAsync();
            }
        }
    }

    private sealed class NamedLineageContext(
        DbContextOptions<NamedLineageContext> options
    ) : DbContext(options)
    {
        public DbSet<NamedLineageTarget> Targets => Set<NamedLineageTarget>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.SharedTypeEntity<NamedLineageNode>("First", entity =>
            {
                entity.ToTable(NamedFirstTable);
                entity.HasKey(row => row.Id);
                entity.Property(row => row.Id).ValueGeneratedNever();
                entity.Property(row => row.TargetId).HasColumnName("FirstTargetId");
                entity.HasOne(row => row.Target).WithMany().HasForeignKey(row => row.TargetId);
            });
            modelBuilder.SharedTypeEntity<NamedLineageNode>("Second", entity =>
            {
                entity.ToTable(NamedSecondTable);
                entity.HasKey(row => row.Id);
                entity.Property(row => row.Id).ValueGeneratedNever();
                entity.Property(row => row.TargetId).HasColumnName("SecondTargetId");
                entity.HasOne(row => row.Target).WithMany().HasForeignKey(row => row.TargetId);
            });
            modelBuilder.Entity<NamedLineageTarget>(entity =>
            {
                entity.ToTable(NamedTargetTable);
                entity.Property(row => row.Id).ValueGeneratedNever();
                entity.Property(row => row.Name).HasMaxLength(64);
            });
        }
    }

    private sealed class NamedLineageNode
    {
        public int Id { get; set; }

        public int TargetId { get; set; }

        public NamedLineageTarget Target { get; set; } = null!;
    }

    private sealed class NamedLineageTarget
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }
}
