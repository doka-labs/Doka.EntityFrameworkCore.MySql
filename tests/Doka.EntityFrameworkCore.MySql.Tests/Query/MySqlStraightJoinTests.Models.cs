namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlStraightJoinTests
{
    /// <summary>Creates deterministic configured services without opening a database connection.</summary>
    private static JoinContext CreateContext(bool mariaDb = false, bool relationalNulls = false, IServiceProvider? services = null)
    {
        var blocker = new OpeningBlocker();
        var builder = new DbContextOptionsBuilder<JoinContext>()
            .UseMySql("Server=127.0.0.1;Database=straight_join_unused;User ID=unused;Password=unused;",
                mariaDb ? MySqlServerVersion.MariaDb(new Version(11, 8, 8)) : MySqlServerVersion.MySql(new Version(8, 4, 11)),
                options => options.UseRelationalNulls(relationalNulls))
            .AddInterceptors(blocker);

        if (services is not null)
        {
            builder.UseInternalServiceProvider(services);
        }

        return new JoinContext(builder.Options, blocker);
    }

    /// <summary>Hosts application filters, converted keys and both collection-shaping paths.</summary>
    private sealed class JoinContext(DbContextOptions<JoinContext> options, OpeningBlocker blocker) : DbContext(options)
    {
        /// <summary>Gets the controlled I/O boundary observed by every probe.</summary>
        public OpeningBlocker Blocker { get; } = blocker;

        /// <summary>Gets the instance-bound filter input rather than a model-global tenant constant.</summary>
        public Guid TenantId { get; } = Guid.Parse("11111111-1111-1111-1111-111111111111");

        /// <summary>Gets the mapped owner query root.</summary>
        public DbSet<JoinNode> Nodes => Set<JoinNode>();

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var nodes = modelBuilder.Entity<JoinNode>();
            nodes.HasKey(value => value.Id);
            nodes.Property(value => value.Code).HasConversion(value => value.Value, value => new JoinCode(value));
            nodes.HasQueryFilter(value => value.Visible && value.TenantId == TenantId);
            nodes.OwnsOne(value => value.Details);
            nodes.OwnsMany(value => value.Tags, tags =>
            {
                tags.WithOwner().HasForeignKey("OwnerId");
                tags.Property<int>("TagId");
                tags.HasKey("OwnerId", "TagId");
            });
            nodes.HasOne(value => value.Parent).WithMany().HasForeignKey(value => value.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
            nodes.HasMany(value => value.Items).WithOne().HasForeignKey(value => value.OwnerId);
        }
    }

    /// <summary>Supplies representative duplicate-capable join keys and mapped application payload.</summary>
    private sealed class JoinNode
    {
        /// <summary>Gets or sets the owner identity.</summary>
        public int Id { get; set; }
        /// <summary>Gets or sets the application tenant.</summary>
        public Guid TenantId { get; set; }
        /// <summary>Gets or sets the duplicate-capable optional key.</summary>
        public int? OptionalKey { get; set; }
        /// <summary>Gets or sets an optional parent reference key.</summary>
        public int? ParentId { get; set; }
        /// <summary>Gets or sets an ordinary optional self-reference navigation.</summary>
        public JoinNode? Parent { get; set; }
        /// <summary>Gets or sets the converted operator-free key.</summary>
        public JoinCode Code { get; set; }
        /// <summary>Gets or sets the application visibility predicate.</summary>
        public bool Visible { get; set; }
        /// <summary>Gets or sets the owned reference payload.</summary>
        public JoinDetails Details { get; set; } = new();
        /// <summary>Gets the automatically included owned collection.</summary>
        public List<JoinTag> Tags { get; } = [];
        /// <summary>Gets the ordinary related collection.</summary>
        public List<JoinItem> Items { get; } = [];
    }

    /// <summary>Represents a converted key without custom expression comparison operators.</summary>
    private readonly record struct JoinCode(Guid Value);

    /// <summary>Represents an owned reference projection.</summary>
    private sealed class JoinDetails
    {
        /// <summary>Gets or sets the reference payload.</summary>
        public string Text { get; set; } = string.Empty;
    }

    /// <summary>Represents one occurrence in an owned collection.</summary>
    private sealed class JoinTag
    {
        /// <summary>Gets or sets the collection payload.</summary>
        public string Text { get; set; } = string.Empty;
    }

    /// <summary>Represents an ordinary collection with its own stable identifier.</summary>
    private sealed class JoinItem
    {
        /// <summary>Gets or sets the occurrence identifier.</summary>
        public int Id { get; set; }
        /// <summary>Gets or sets the owning node key.</summary>
        public int OwnerId { get; set; }
        /// <summary>Gets or sets the ordinary collection payload.</summary>
        public string Text { get; set; } = string.Empty;
    }

    /// <summary>Identifies the intentional no-I/O stopping point after SQL translation.</summary>
    private sealed class ConnectionBlockedException : Exception;

    /// <summary>Stops both execution paths before any database connection can open.</summary>
    private sealed class OpeningBlocker : DbConnectionInterceptor
    {
        /// <summary>Gets the number of correctly reached execution boundaries.</summary>
        public int Openings { get; private set; }

        /// <inheritdoc />
        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result)
        {
            Openings++;

            throw new ConnectionBlockedException();
        }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
            ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Openings++;

            throw new ConnectionBlockedException();
        }
    }
}
