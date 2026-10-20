namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlMaterializedSourceIntegrationTests
{
    private const string ItemTable = "IntBoundSourceItems";
    private const string ChildTable = "IntBoundSourceChildren";
    private static readonly Guid s_binaryToken = new("00112233-4455-6677-8899-aabbccddeeff");

    private static BindingContext CreateContext(
        IntegrationDatabaseTarget target,
        IInterceptor interceptor
    )
    {
        var options = IntegrationTestDbContextOptions.Create<BindingContext>()
            .UseMySql(
                IntegrationTestEnvironment.GetConnectionString(target),
                IntegrationTestEnvironment.GetServerVersion(target))
            .AddInterceptors(interceptor)
            .Options;

        return new BindingContext(options);
    }

    private static async Task<BindingFixture> CreateFixtureAsync(
        IntegrationDatabaseTarget target
    )
    {
        var probe = new BindingCommandProbe();
        var context = CreateContext(target, probe);

        try
        {
            await DropTablesAsync(context);
            await context.Database.ExecuteSqlRawAsync(context.Database.GenerateCreateScript(), CancellationToken.None);

            for (var id = 1; id <= 32; id++)
            {
                var item = new BoundItem
                {
                    Id = id,
                    Name = id == 1 ? "ALPHA" : id == 2 ? "alpha" : $"item-{id}",
                    Token = id == 1 ? s_binaryToken : Guid.Empty,
                    OptionalScore = id == 1 ? null : id,
                    Code = new BoundCode($"code-{id}"),
                };

                if (id <= 2)
                {
                    item.Children.Add(new BoundChild
                    {
                        Id = id,
                        Name = $"child-{id}",
                    });
                }

                if (id == 1)
                {
                    item.Children.Add(new BoundChild
                    {
                        Id = 3,
                        Name = "child-1b",
                    });
                }

                context.Add(item);
            }

            await context.SaveChangesAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            probe.Reset();

            return new BindingFixture(context, probe);
        }
        catch
        {
            await context.DisposeAsync();

            throw;
        }
    }

    private static Task<int> DropTablesAsync(
        BindingContext context
    ) => context.Database.ExecuteSqlRawAsync(
        $"DROP TABLE IF EXISTS `{ChildTable}`; DROP TABLE IF EXISTS `{ItemTable}`;",
        CancellationToken.None);

    private sealed class BindingFixture(
        BindingContext context,
        BindingCommandProbe probe
    ) : IAsyncDisposable
    {
        public BindingContext Context { get; } = context;

        public BindingCommandProbe Probe { get; } = probe;

        public async ValueTask DisposeAsync()
        {
            try
            {
                await DropTablesAsync(Context);
            }
            finally
            {
                await Context.DisposeAsync();
            }
        }
    }

    /// <summary>Observes the real relational reader boundary without changing the statement.</summary>
    private sealed class BindingCommandProbe : DbCommandInterceptor
    {
        public int Count { get; private set; }

        public CancellationToken Token { get; private set; }

        public string CommandText { get; private set; } = string.Empty;

        public object?[] ParameterValues { get; private set; } = [];

        public void Reset()
        {
            Count = 0;
            Token = CancellationToken.None;
            CommandText = string.Empty;
            ParameterValues = [];
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Count++;
            Token = cancellationToken;
            CommandText = command.CommandText;
            ParameterValues = command.Parameters.Cast<DbParameter>()
                .Select(parameter => parameter.Value)
                .ToArray();

            return new ValueTask<InterceptionResult<DbDataReader>>(result);
        }
    }

    private sealed class BindingContext(
        DbContextOptions<BindingContext> options
    ) : DbContext(options)
    {
        public DbSet<BoundItem> Items => Set<BoundItem>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.HasCharSet("utf8mb4");
            modelBuilder.Entity<BoundItem>(entity =>
            {
                entity.ToTable(ItemTable);
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).ValueGeneratedNever();
                entity.Property(item => item.Name)
                    .HasMaxLength(64)
                    .UseCollation("utf8mb4_general_ci");
                entity.Property(item => item.Token).HasMySqlGuidFormat(MySqlGuidFormat.Binary16);
                entity.Property(item => item.Code)
                    .HasConversion(value => value.Value, value => new BoundCode(value))
                    .HasMaxLength(64);
                entity.HasMany(item => item.Children)
                    .WithOne()
                    .HasForeignKey(child => child.ItemId);
            });
            modelBuilder.Entity<BoundChild>(entity =>
            {
                entity.ToTable(ChildTable);
                entity.HasKey(child => child.Id);
                entity.Property(child => child.Id).ValueGeneratedNever();
                entity.Property(child => child.Name).HasMaxLength(64);
            });
        }
    }

    private sealed class BoundItem
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public Guid Token { get; set; }

        public int? OptionalScore { get; set; }

        public BoundCode Code { get; set; } = new(string.Empty);

        public List<BoundChild> Children { get; set; } = [];
    }

    private sealed class BoundChild
    {
        public int Id { get; set; }

        public int ItemId { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    // WHY: This operator-free reference type requires the SQL comparison to retain its converter.
    private sealed class BoundCode(
        string value
    )
    {
        public string Value { get; } = value;
    }

    private sealed class BoundSample
    {
        public int Id { get; set; }

        public Guid Stamp { get; set; }

        public double Sample { get; set; }
    }

    private sealed class BoundPair
    {
        public int LeftId { get; set; }

        public int RightId { get; set; }

        public Guid LeftStamp { get; set; }

        public Guid RightStamp { get; set; }

        public double LeftSample { get; set; }

        public double RightSample { get; set; }
    }

    private sealed class CollectionResult
    {
        public int Id { get; set; }

        public string[] Children { get; set; } = [];
    }
}
