namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMaterializedSourceTranslationTests
{
    /// <summary>
    /// Creates an offline translation context with an explicitly configured database family.
    /// </summary>
    private static BindingContext CreateContext(
        bool mariaDb,
        bool splitQueryDefault = false
    )
    {
        var options = MySqlFunctionalTestOptions.CreateTransientBuilder<BindingContext>()
            .UseMySql(
                "Server=localhost;Database=materialized_translation;User ID=unused;",
                CreateServerVersion(mariaDb),
                options =>
                {
                    if (splitQueryDefault)
                    {
                        options.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                    }
                })
            .Options;

        return new BindingContext(options);
    }

    /// <summary>
    /// Creates an isolated model whose collection navigation is automatically included.
    /// </summary>
    private static AutoIncludeContext CreateAutoIncludeContext(
        bool mariaDb
    )
    {
        var options = MySqlFunctionalTestOptions.CreateTransientBuilder<AutoIncludeContext>()
            .UseMySql(
                "Server=localhost;Database=materialized_translation;User ID=unused;",
                CreateServerVersion(mariaDb))
            .Options;

        return new AutoIncludeContext(options);
    }

    /// <summary>Gets the explicitly configured family used by both translation contexts.</summary>
    private static MySqlServerVersion CreateServerVersion(
        bool mariaDb
    )
        => mariaDb
            ? MySqlServerVersion.MariaDb(new Version(11, 8, 0))
            : MySqlServerVersion.MySql(new Version(8, 4, 0));

    /// <summary>
    /// Separates the automatic Include model from the ordinary context's cached model identity.
    /// </summary>
    private sealed class AutoIncludeContext : DbContext
    {
        /// <summary>Initializes the automatic collection Include model.</summary>
        public AutoIncludeContext(DbContextOptions<AutoIncludeContext> options) : base(options) { }

        /// <summary>Gets entities whose collection is included unless the query opts out.</summary>
        public DbSet<BindingNode> Nodes => Set<BindingNode>();

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<BindingNode>(entity =>
            {
                entity.ToTable("BindingNodes");
                entity.HasKey(node => node.Id);
                entity.Property(node => node.Token).HasColumnType("binary(16)");
                entity.HasMany(node => node.Children).WithOne(child => child.Node).HasForeignKey(child => child.NodeId);
                entity.Navigation(node => node.Children).AutoInclude();
            });

            modelBuilder.Entity<BindingChild>().ToTable("BindingChildren");
        }
    }

    /// <summary>
    /// Contains independent mapping shapes so each test exercises only its selected source.
    /// </summary>
    private sealed class BindingContext : DbContext
    {
        /// <summary>Initializes a translation-only context.</summary>
        public BindingContext(DbContextOptions<BindingContext> options) : base(options) { }

        /// <summary>Gets ordinary entities with native scalar payload.</summary>
        public DbSet<BindingNode> Nodes => Set<BindingNode>();

        /// <summary>Gets the physical table used to exercise source-name collisions.</summary>
        public DbSet<CollisionRecord> Collisions => Set<CollisionRecord>();

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<BindingNode>(entity =>
            {
                entity.ToTable("BindingNodes");
                entity.HasKey(node => node.Id);
                entity.Property(node => node.Token).HasColumnType("binary(16)");
                entity.Property(node => node.Name).HasMaxLength(80);
                entity.Property(node => node.OptionalName).HasMaxLength(80).UseCollation("utf8mb4_general_ci");
                entity.HasMany(node => node.Children).WithOne(child => child.Node).HasForeignKey(child => child.NodeId);
            });

            modelBuilder.Entity<BindingChild>().ToTable("BindingChildren");
            modelBuilder.Entity<CollisionRecord>().ToTable("__doka_source_0");

            modelBuilder.Entity<TptRoot>().UseTptMappingStrategy().ToTable("BindingTptRoots");
            modelBuilder.Entity<TptLeaf>().ToTable("BindingTptLeaves");
            modelBuilder.Entity<TpcRoot>().UseTpcMappingStrategy();
            modelBuilder.Entity<TpcFirst>().ToTable("BindingTpcFirst");
            modelBuilder.Entity<TpcSecond>().ToTable("BindingTpcSecond");

            modelBuilder.Entity<SplitRecord>(entity =>
            {
                entity.ToTable("BindingSplitRecords");
                entity.SplitToTable("BindingSplitPayload", table => table.Property(row => row.Extra));
            });

            modelBuilder.Entity<SharedRecord>(entity =>
            {
                entity.ToTable("BindingSharedRecords");
                entity.HasOne(row => row.Details).WithOne().HasForeignKey<SharedDetails>(row => row.Id);
                entity.Navigation(row => row.Details).IsRequired();
            });

            modelBuilder.Entity<SharedDetails>().ToTable("BindingSharedRecords");

            modelBuilder.Entity<OwnedRecord>(entity =>
            {
                entity.ToTable("BindingOwnedRecords");
                entity.OwnsOne(row => row.Profile, owned => owned.Property(profile => profile.Token)
                    .HasColumnType("binary(16)"));
                entity.OwnsMany(row => row.Tags, owned =>
                {
                    owned.ToTable("BindingOwnedTags");
                    owned.WithOwner().HasForeignKey("OwnerId");
                    owned.HasKey("OwnerId", nameof(OwnedTag.Id));
                });
            });

            modelBuilder.Entity<ShadowRecord>(entity =>
            {
                entity.ToTable("BindingShadowRecords");
                entity.Property<Guid>("Scope").HasColumnType("binary(16)");
                entity.HasKey("Scope", nameof(ShadowRecord.Id));
            });

            modelBuilder.Entity<ConvertedRecord>(entity =>
            {
                entity.ToTable("BindingConvertedRecords");
                entity.Property(row => row.Reference)
                    .HasConversion(value => value.Value, value => new ConvertedIdentifier(value))
                    .HasColumnType("binary(16)");
            });
        }
    }

    /// <summary>A flat structural projection with no deferred navigation shaping.</summary>
    private sealed class Coordinates
    {
        /// <summary>Gets or initializes the selected key.</summary>
        public int Id { get; init; }

        /// <summary>Gets or initializes the first bound.</summary>
        public int Left { get; init; }

        /// <summary>Gets or initializes the second bound.</summary>
        public int Right { get; init; }
    }

    /// <summary>An ordinary entity with scalar structural and payload columns.</summary>
    private sealed class BindingNode
    {
        /// <summary>Gets or sets the key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the first bound.</summary>
        public int Left { get; set; }

        /// <summary>Gets or sets the second bound.</summary>
        public int Right { get; set; }

        /// <summary>Gets or sets the native binary GUID.</summary>
        public Guid Token { get; set; }

        /// <summary>Gets or sets the ordering payload.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets a nullable, explicitly collated payload.</summary>
        public string? OptionalName { get; set; }

        /// <summary>Gets the collection used only in consumer result shaping.</summary>
        public List<BindingChild> Children { get; } = [];
    }

    /// <summary>A dependent row that requires EF collection identifiers.</summary>
    private sealed class BindingChild
    {
        /// <summary>Gets or sets the dependent key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the principal key.</summary>
        public int NodeId { get; set; }

        /// <summary>Gets or sets the principal used by chained Include rejection tests.</summary>
        public BindingNode Node { get; set; } = null!;

        /// <summary>Gets or sets the dependent payload.</summary>
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>A physical table whose name resembles a materialized-source alias.</summary>
    private sealed class CollisionRecord
    {
        /// <summary>Gets or sets the key.</summary>
        public int Id { get; set; }
    }

    /// <summary>A TPT principal with inherited payload.</summary>
    private abstract class TptRoot
    {
        /// <summary>Gets or sets the hierarchy key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the principal-table payload.</summary>
        public string Label { get; set; } = string.Empty;
    }

    /// <summary>A TPT derived table with its own payload.</summary>
    private sealed class TptLeaf : TptRoot
    {
        /// <summary>Gets or sets the derived-table payload.</summary>
        public int Score { get; set; }
    }

    /// <summary>A TPC principal whose concrete tables form a completed UNION ALL source.</summary>
    private abstract class TpcRoot
    {
        /// <summary>Gets or sets the hierarchy key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the common payload.</summary>
        public string Label { get; set; } = string.Empty;
    }

    /// <summary>The first concrete TPC branch.</summary>
    private sealed class TpcFirst : TpcRoot { }

    /// <summary>The second concrete TPC branch.</summary>
    private sealed class TpcSecond : TpcRoot { }

    /// <summary>An entity whose payload occupies two physical tables.</summary>
    private sealed class SplitRecord
    {
        /// <summary>Gets or sets the shared physical key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the primary-table payload.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the secondary-table payload.</summary>
        public string Extra { get; set; } = string.Empty;
    }

    /// <summary>A principal sharing its physical table with a separate dependent entity.</summary>
    private sealed class SharedRecord
    {
        /// <summary>Gets or sets the shared key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the principal payload.</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>Gets or sets the required table-split dependent.</summary>
        public SharedDetails Details { get; set; } = null!;
    }

    /// <summary>The dependent part of a table-split row.</summary>
    private sealed class SharedDetails
    {
        /// <summary>Gets or sets the shared principal key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the dependent payload.</summary>
        public string Value { get; set; } = string.Empty;
    }

    /// <summary>An entity with an owned reference whose scalars can be projected.</summary>
    private sealed class OwnedRecord
    {
        /// <summary>Gets or sets the owner key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the required owned reference.</summary>
        public OwnedProfile Profile { get; set; } = new();

        /// <summary>Gets the owned collection used only in result shaping.</summary>
        public List<OwnedTag> Tags { get; } = [];
    }

    /// <summary>An owned collection row with an explicit occurrence identifier.</summary>
    private sealed class OwnedTag
    {
        /// <summary>Gets or sets the identifier within the owner.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the collection payload.</summary>
        public string Value { get; set; } = string.Empty;
    }

    /// <summary>A flat set of owned reference values.</summary>
    private sealed class OwnedProfile
    {
        /// <summary>Gets or sets the SQL-native GUID.</summary>
        public Guid Token { get; set; }

        /// <summary>Gets or sets the owned nullable payload.</summary>
        public string? Note { get; set; }
    }

    /// <summary>A composite-key entity whose other key component exists only in the model.</summary>
    private sealed class ShadowRecord
    {
        /// <summary>Gets or sets the CLR component of the composite key.</summary>
        public int Id { get; set; }
    }

    /// <summary>An entity using a converter without an overloaded CLR equality operator.</summary>
    private sealed class ConvertedRecord
    {
        /// <summary>Gets or sets the ordinary entity key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the converted binary value used by the SQL join.</summary>
        public ConvertedIdentifier Reference { get; set; } = new(Guid.Empty);
    }

    /// <summary>A converted reference value that deliberately declares no equality operator.</summary>
    private sealed class ConvertedIdentifier
    {
        /// <summary>Initializes the mapped value.</summary>
        public ConvertedIdentifier(Guid value)
        {
            Value = value;
        }

        /// <summary>Gets the native GUID consumed by the converter.</summary>
        public Guid Value { get; }
    }
}
