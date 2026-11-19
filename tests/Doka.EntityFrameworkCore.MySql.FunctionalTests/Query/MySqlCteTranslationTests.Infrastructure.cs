namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlCteTranslationTests
{
    /// <summary>Creates a translation-only context through the existing transient-provider test policy.</summary>
    private static CteContext CreateContext(
        bool mariaDb
    )
    {
        var version = mariaDb
            ? MySqlServerVersion.MariaDb(new Version(12, 3, 3))
            : MySqlServerVersion.MySql(new Version(8, 4, 0));

        var options = MySqlFunctionalTestOptions
            .CreateTransientBuilder<CteContext>()
            .UseMySql("Server=localhost;Database=cte_translation;User ID=unused;", version)
            .Options;

        return new CteContext(options);
    }

    /// <summary>Contains independently filtered roots and scalar mappings required by CTE composition.</summary>
    private sealed class CteContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public CteContext(
            DbContextOptions<CteContext> options
        ) : base(options) { }

        /// <summary>Gets the context-scoped tenant parameter retained in source definitions.</summary>
        public int TenantId { get; set; } = 7;

        /// <summary>Gets the ordinary source entity set.</summary>
        public DbSet<CteRow> Rows => Set<CteRow>();

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<CteRow>(entity =>
            {
                entity.ToTable("CteRows");
                entity.HasKey(row => row.Id);
                entity.HasQueryFilter(row => row.TenantId == TenantId);
                entity
                    .Property(row => row.Name)
                    .HasMaxLength(100);

                entity
                    .Property(row => row.OptionalName)
                    .HasMaxLength(100)
                    .UseCollation("utf8mb4_unicode_ci");

                entity
                    .Property(row => row.UnboundedName)
                    .HasColumnType("longtext");

                entity
                    .Property(row => row.Latin1Name)
                    .HasMaxLength(100)
                    .UseCollation("latin1_swedish_ci");

                entity
                    .Property(row => row.Token)
                    .HasColumnType("binary(16)");

                entity
                    .Property(row => row.Amount)
                    .HasPrecision(18, 4);

                entity
                    .Property(row => row.OccurredAt)
                    .HasPrecision(6);

                entity
                    .Property<Guid>("ShadowToken")
                    .HasColumnType("char(36)");

                entity
                    .Property<string>("_fieldValue")
                    .HasColumnName("FieldValue")
                    .HasMaxLength(120);

                entity
                    .HasOne(row => row.Target)
                    .WithMany()
                    .HasForeignKey(row => row.TargetId);
            });

            modelBuilder.Entity<CteTarget>(entity =>
            {
                entity.ToTable("CteTargets");
                entity.HasKey(row => row.Id);
                entity.HasQueryFilter(row => row.IsVisible);
                entity
                    .Property(row => row.Name)
                    .HasMaxLength(100);
            });

            modelBuilder.Entity<CteWideRow>(entity =>
            {
                entity.ToTable("CteWideRows");
                entity.HasKey(row => row.Id);

                // WHY: Shadow columns exercise the actual fifty-column EF schema without a second
                // fifty-property CLR fixture or a reflection-based test-only projection language.
                for (var index = 1; index <= 48; index++)
                {
                    entity.Property<int>("Payload" + index.ToString("D2", CultureInfo.InvariantCulture));
                }
            });
        }
    }

    /// <summary>Models native scalar facets and an independently filtered reference target.</summary>
    private sealed class CteRow
    {
        private string _fieldValue = string.Empty;

        /// <summary>Gets or sets the physical source key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the native parent edge used by recursive tree sources.</summary>
        public int? ParentId { get; set; }

        /// <summary>Gets or sets the tenant used by the source filter.</summary>
        public int TenantId { get; set; }

        /// <summary>Gets or sets the nonnullable text payload.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets explicitly collated nullable text.</summary>
        public string? OptionalName { get; set; }

        /// <summary>Gets or sets text whose physical LONGTEXT capacity is not a packet-size proof.</summary>
        public string UnboundedName { get; set; } = string.Empty;

        /// <summary>Gets or sets a physical text override which must not be silently converted before its requested COLLATE.</summary>
        public string Latin1Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the native binary GUID payload.</summary>
        public Guid Token { get; set; }

        /// <summary>Gets or sets a fixed-scale decimal payload.</summary>
        public decimal Amount { get; set; }

        /// <summary>Gets or sets the fractional temporal payload.</summary>
        public DateTime OccurredAt { get; set; }

        /// <summary>Gets or sets the physical foreign key.</summary>
        public int TargetId { get; set; }

        /// <summary>Gets or sets the target whose own filter must survive the lexical root visit.</summary>
        public CteTarget Target { get; set; } = null!;
    }

    /// <summary>Provides a reference filter independent of the CTE source's tenant filter.</summary>
    private sealed class CteTarget
    {
        /// <summary>Gets or sets the target key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the target filter state.</summary>
        public bool IsVisible { get; set; }

        /// <summary>Gets or sets the projected navigation payload.</summary>
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>Supplies a fifty-column physical schema, including forty-eight mapped shadow payloads.</summary>
    private sealed class CteWideRow
    {
        /// <summary>Gets or sets the physical identity retained by recursive parent matching.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the physical parent edge independent of the returned payload.</summary>
        public int? ParentId { get; set; }
    }
}
