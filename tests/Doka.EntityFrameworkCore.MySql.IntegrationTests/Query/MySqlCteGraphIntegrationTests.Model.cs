namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlCteGraphIntegrationTests
{
    /// <summary>Uses actual context-scoped filters with an options-only constructor suitable for EF context pooling.</summary>
    private sealed class GraphContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public GraphContext(
            DbContextOptions<GraphContext> options
        ) : base(options) { }

        /// <summary>Gets or sets the explicitly assigned tenant of this context lease.</summary>
        public int TenantId { get; set; } = 1;

        /// <summary>Gets or sets the cold model fixture's alternative physical vertex mapping.</summary>
        public bool AlternateVertices { get; init; }

        /// <summary>Gets the graph vertices.</summary>
        public DbSet<GraphVertex> Vertices => Set<GraphVertex>();

        /// <summary>Gets the independent edge occurrences.</summary>
        public DbSet<GraphEdge> Edges => Set<GraphEdge>();

        /// <summary>Gets the separately filtered targets.</summary>
        public DbSet<GraphTarget> Targets => Set<GraphTarget>();

        /// <summary>Gets exact mapped scalar fixtures.</summary>
        public DbSet<GraphFacet> Facets => Set<GraphFacet>();

        /// <summary>Gets the independently encoded physical source.</summary>
        public DbSet<GraphLatinRow> LatinRows => Set<GraphLatinRow>();

        /// <summary>Maps a test-only native sleep used solely by the server-observed cancellation case.</summary>
        public static int NativeSleep(
            int seconds
        ) => throw new InvalidOperationException("A native SQL function cannot run on the client.");

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.HasCharSet("utf8mb4");
            modelBuilder.UseCollation("utf8mb4_unicode_ci");
            modelBuilder
                .HasDbFunction(typeof(GraphContext).GetMethod(nameof(NativeSleep))!)
                .HasName("SLEEP")
                .IsBuiltIn();

            modelBuilder.Entity<GraphVertex>(entity =>
            {
                entity
                    .ToTable(AlternateVertices
                        ? "GraphVerticesAlternate"
                        : "GraphVertices")
                    .UseStorageEngine("InnoDB");

                entity
                    .Property(row => row.Id)
                    .ValueGeneratedNever();

                entity
                    .Property(row => row.Name)
                    .HasMaxLength(80);

                entity
                    .Property<string>("_note")
                    .HasColumnName("FieldNote")
                    .HasMaxLength(80);

                entity.Property<int>("ShadowNumber");
                entity.HasQueryFilter(row => row.TenantId == TenantId);
                entity
                    .HasDiscriminator<string>("Kind")
                    .HasValue<GraphVertex>("base")
                    .HasValue<SpecialVertex>("special");

                entity
                    .HasOne(row => row.Target)
                    .WithMany(target => target.Vertices)
                    .HasForeignKey(row => row.TargetId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder
                .Entity<SpecialVertex>()
                .Property(row => row.Tag)
                .HasMaxLength(40);

            modelBuilder.Entity<GraphEdge>(entity =>
            {
                entity
                    .ToTable("GraphEdges")
                    .UseStorageEngine("InnoDB");

                entity
                    .Property(row => row.Id)
                    .ValueGeneratedNever();

                entity.HasQueryFilter(row => row.TenantId == TenantId);
            });

            modelBuilder.Entity<GraphTarget>(entity =>
            {
                entity
                    .ToTable("GraphTargets")
                    .UseStorageEngine("InnoDB");

                entity
                    .Property(row => row.Id)
                    .ValueGeneratedNever();

                entity
                    .Property(row => row.Name)
                    .HasMaxLength(80);

                entity.HasQueryFilter(row => row.IsVisible);
            });

            modelBuilder.Entity<GraphFacet>(entity =>
            {
                entity
                    .ToTable("GraphFacets")
                    .UseStorageEngine("InnoDB");

                entity
                    .Property(row => row.Id)
                    .ValueGeneratedNever();

                entity
                    .Property(row => row.BinaryId)
                    .HasColumnType("binary(16)");

                entity
                    .Property(row => row.CharId)
                    .HasColumnType("char(36)");

                entity
                    .Property(row => row.NativeId)
                    .HasColumnType(Database.IsMariaDb()
                        ? "uuid"
                        : "char(36)");

                entity
                    .Property(row => row.OptionalId)
                    .HasColumnType("char(36)");

                entity
                    .Property(row => row.Amount)
                    .HasPrecision(20, 4);

                entity
                    .Property(row => row.Moment)
                    .HasPrecision(6);

                entity
                    .Property(row => row.State)
                    .HasConversion<string>()
                    .HasMaxLength(16);

                entity
                    .Property(row => row.Code)
                    .HasConversion(value => value.Value, value => new GraphCode(value))
                    .HasMaxLength(32);

                entity
                    .Property(row => row.Label)
                    .HasMaxLength(80)
                    .UseCollation("utf8mb4_unicode_ci");
            });

            modelBuilder.Entity<GraphLatinRow>(entity =>
            {
                entity
                    .ToTable("GraphLatinRows")
                    .UseStorageEngine("InnoDB");

                entity.HasCharSet("latin1");
                entity
                    .Property(row => row.Id)
                    .ValueGeneratedNever();

                entity
                    .Property(row => row.Value)
                    .HasColumnType("varchar(80)")
                    .UseCollation("latin1_swedish_ci");
            });
        }
    }
}
