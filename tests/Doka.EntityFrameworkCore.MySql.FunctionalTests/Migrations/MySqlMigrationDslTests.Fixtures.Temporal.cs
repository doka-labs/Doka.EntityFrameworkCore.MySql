namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMigrationDslTests
{
    /// <summary>
    /// Identifies native temporal schema changes that cannot preserve retained history safely.
    /// </summary>
    public enum NativeTemporalSchemaChange
    {
        /// <summary>Adds a regular column.</summary>
        AddColumn,
        /// <summary>Renames a regular column.</summary>
        RenameColumn,
        /// <summary>Changes a regular column definition.</summary>
        AlterColumn,
        /// <summary>Drops a regular column.</summary>
        DropColumn,
        /// <summary>Renames the temporal table.</summary>
        RenameTable,
    }

    private static (DbContext Source, DbContext Target) CreateNativeTemporalSchemaChangeContexts(
        NativeTemporalSchemaChange schemaChange,
        MySqlServerVersion serverVersion
    ) => schemaChange switch
    {
        NativeTemporalSchemaChange.AddColumn => (
            new TemporalSchemaContext(CreateOptions<TemporalSchemaContext>(serverVersion)),
            new TemporalSchemaWithDescriptionContext(
                CreateOptions<TemporalSchemaWithDescriptionContext>(serverVersion))),
        NativeTemporalSchemaChange.RenameColumn => (
            new TemporalSchemaContext(CreateOptions<TemporalSchemaContext>(serverVersion)),
            new TemporalSchemaWithRenamedColumnContext(
                CreateOptions<TemporalSchemaWithRenamedColumnContext>(serverVersion))),
        NativeTemporalSchemaChange.AlterColumn => (
            new TemporalSchemaContext(CreateOptions<TemporalSchemaContext>(serverVersion)),
            new TemporalSchemaWithBoundedNameContext(
                CreateOptions<TemporalSchemaWithBoundedNameContext>(serverVersion))),
        NativeTemporalSchemaChange.DropColumn => (
            new TemporalSchemaWithDescriptionContext(
                CreateOptions<TemporalSchemaWithDescriptionContext>(serverVersion)),
            new TemporalSchemaContext(CreateOptions<TemporalSchemaContext>(serverVersion))),
        NativeTemporalSchemaChange.RenameTable => (
            new TemporalSchemaContext(CreateOptions<TemporalSchemaContext>(serverVersion)),
            new RenamedTemporalSchemaContext(
                CreateOptions<RenamedTemporalSchemaContext>(serverVersion))),
        _ => throw new ArgumentOutOfRangeException(nameof(schemaChange), schemaChange, null),
    };

    private sealed class TemporalMigrationDslContext : DbContext
    {
        /// <summary>
        /// Initializes the temporal migration dsl context fixture.
        /// </summary>
        public TemporalMigrationDslContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<MigrationDslEntity>(entity =>
        {
            entity.ToTable(
                "MigrationDsl",
                table => table.IsTemporal(temporal =>
                {
                    temporal.UseHistoryTable("MigrationDslHistory");
                    temporal.HasPeriodStart("ValidFrom");
                    temporal.HasPeriodEnd("ValidTo");
                }));
        });
    }

    private sealed class OrdinaryApplicationTimeMigrationDslContext : DbContext
    {
        /// <summary>
        /// Initializes the ordinary application time migration dsl context fixture.
        /// </summary>
        public OrdinaryApplicationTimeMigrationDslContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<MigrationDslEntity>(entity => entity.ToTable("MigrationDsl"));
    }

    private sealed class ApplicationTimeMigrationDslContext : DbContext
    {
        /// <summary>
        /// Initializes the application time migration dsl context fixture.
        /// </summary>
        public ApplicationTimeMigrationDslContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<MigrationDslEntity>(entity =>
        {
            entity.ToTable(
                "MigrationDsl",
                table => table.HasApplicationTimePeriod(applicationTime =>
                {
                    applicationTime.HasPeriodName("BusinessValidity");
                    applicationTime.HasPeriodStart("ValidFrom");
                    applicationTime.HasPeriodEnd("ValidTo");
                    applicationTime.UseWithoutOverlaps();
                }));
        });
    }

    private sealed class BitemporalMigrationDslContext : DbContext
    {
        /// <summary>
        /// Initializes the bitemporal migration dsl context fixture.
        /// </summary>
        public BitemporalMigrationDslContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<MigrationDslEntity>(entity =>
        {
            entity.ToTable(
                "MigrationDsl",
                table => table.IsBitemporal(
                    systemTime =>
                    {
                        systemTime.HasPeriodStart("SystemValidFrom");
                        systemTime.HasPeriodEnd("SystemValidTo");
                    },
                    applicationTime =>
                    {
                        applicationTime.HasPeriodName("BusinessValidity");
                        applicationTime.HasPeriodStart("BusinessValidFrom");
                        applicationTime.HasPeriodEnd("BusinessValidTo");
                    }));
        });
    }

    private sealed class NonTemporalSchemaContext : DbContext
    {
        /// <summary>
        /// Initializes the non-temporal schema context fixture.
        /// </summary>
        public NonTemporalSchemaContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureTemporalSchema(modelBuilder, "TemporalRecords", "TemporalRecordsHistory");
    }

    private sealed class TemporalSchemaContext : DbContext
    {
        /// <summary>
        /// Initializes the temporal schema context fixture.
        /// </summary>
        public TemporalSchemaContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureTemporalSchema(
            modelBuilder,
            "TemporalRecords",
            "TemporalRecordsHistory",
            temporal: true);
    }

    private sealed class TemporalSchemaWithDescriptionContext : DbContext
    {
        /// <summary>
        /// Initializes the temporal schema with description context fixture.
        /// </summary>
        public TemporalSchemaWithDescriptionContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureTemporalSchema(
            modelBuilder,
            "TemporalRecords",
            "TemporalRecordsHistory",
            temporal: true,
            includeDescription: true);
    }

    private sealed class TemporalSchemaWithRenamedColumnContext : DbContext
    {
        /// <summary>
        /// Initializes the temporal schema with renamed column context fixture.
        /// </summary>
        public TemporalSchemaWithRenamedColumnContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureTemporalSchema(
            modelBuilder,
            "TemporalRecords",
            "TemporalRecordsHistory",
            temporal: true,
            nameColumn: "DisplayName");
    }

    private sealed class TemporalSchemaWithBoundedNameContext : DbContext
    {
        /// <summary>
        /// Initializes the temporal schema with bounded name context fixture.
        /// </summary>
        public TemporalSchemaWithBoundedNameContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureTemporalSchema(
            modelBuilder,
            "TemporalRecords",
            "TemporalRecordsHistory",
            temporal: true,
            nameMaxLength: 128);
    }

    private sealed class TemporalSchemaWithGeneratedColumnContext : DbContext
    {
        /// <summary>
        /// Initializes the temporal schema with generated column context fixture.
        /// </summary>
        public TemporalSchemaWithGeneratedColumnContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureTemporalSchema(
            modelBuilder,
            "TemporalRecords",
            "TemporalRecordsHistory",
            temporal: true,
            includeGeneratedNameLength: true);
    }

    private sealed class RenamedTemporalSchemaContext : DbContext
    {
        /// <summary>
        /// Initializes the renamed temporal schema context fixture.
        /// </summary>
        public RenamedTemporalSchemaContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureTemporalSchema(
            modelBuilder,
            "TemporalEntries",
            "TemporalEntriesHistory",
            temporal: true);
    }

    private static void ConfigureTemporalSchema(
        ModelBuilder modelBuilder,
        string tableName,
        string historyTableName,
        bool temporal = false,
        bool includeDescription = false,
        string nameColumn = "Name",
        int? nameMaxLength = null,
        bool includeGeneratedNameLength = false
    )
    {
        modelBuilder.Entity<TemporalSchemaEntity>(entity =>
        {
            if (temporal)
            {
                entity.ToTable(
                    tableName,
                    table => table.IsTemporal(temporalTable =>
                    {
                        temporalTable.UseHistoryTable(historyTableName);
                        temporalTable.HasPeriodStart("ValidFrom");
                        temporalTable.HasPeriodEnd("ValidTo");
                    }));
            }
            else
            {
                entity.ToTable(tableName);
            }

            var nameProperty = entity
                .Property(item => item.Name)
                .HasColumnName(nameColumn);

            if (nameMaxLength is not null)
            {
                nameProperty.HasMaxLength(nameMaxLength.Value);
            }

            if (!includeDescription)
            {
                entity.Ignore(item => item.Description);
            }

            if (includeGeneratedNameLength)
            {
                entity
                    .Property(item => item.NameLength)
                    .HasComputedColumnSql("CHAR_LENGTH(`Name`)", stored: true);
            }
            else
            {
                entity.Ignore(item => item.NameLength);
            }
        });
    }

    private sealed class TemporalSchemaEntity
    {
        /// <summary>
        /// Gets or sets the entity key.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the description.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Gets or sets the name length.
        /// </summary>
        public int NameLength { get; set; }
    }
}
