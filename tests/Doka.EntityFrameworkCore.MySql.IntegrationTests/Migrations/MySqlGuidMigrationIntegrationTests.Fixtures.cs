using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlGuidMigrationIntegrationTests
{
    private abstract class GuidStorageContext(DbContextOptions options) : DbContext(options)
    {
        /// <summary>
        /// Verifies set.
        /// </summary>
        public DbSet<GuidStorageRecord> Records => Set<GuidStorageRecord>();

        protected abstract MySqlGuidFormat GuidFormat { get; }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<GuidStorageRecord>(entity =>
            {
                entity.ToTable(StorageTransitionTable);
                entity.HasKey(record => record.Id);
                entity.Property(record => record.Id).UseMySqlAutoIncrementColumn();
                entity.Property(record => record.RequiredValue).HasMySqlGuidFormat(GuidFormat);
                entity.Property(record => record.OptionalValue).HasMySqlGuidFormat(GuidFormat);
            });
        }
    }

    private sealed class Char36StorageContext(DbContextOptions<Char36StorageContext> options)
        : GuidStorageContext(options)
    {
        /// <inheritdoc />
        protected override MySqlGuidFormat GuidFormat => MySqlGuidFormat.Char36;
    }

    private sealed class Binary16StorageContext(DbContextOptions<Binary16StorageContext> options)
        : GuidStorageContext(options)
    {
        /// <inheritdoc />
        protected override MySqlGuidFormat GuidFormat => MySqlGuidFormat.Binary16;
    }

    private sealed class GuidStorageRecord
    {
        /// <summary>
        /// Gets or sets the id.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the required value.
        /// </summary>
        public Guid RequiredValue { get; set; }

        /// <summary>
        /// Gets or sets the optional value.
        /// </summary>
        public Guid? OptionalValue { get; set; }
    }

    private sealed class EmptyGuidContext(DbContextOptions<EmptyGuidContext> options) : DbContext(options);

    private abstract class GuidMigrationContext(DbContextOptions options) : DbContext(options)
    {
        /// <summary>
        /// Verifies set.
        /// </summary>
        public DbSet<TextGuidDocument> Documents => Set<TextGuidDocument>();

        protected abstract bool UseNativeChar36 { get; }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<TextGuidDocument>(entity =>
            {
                entity.ToTable("TextGuidDocuments");
                entity.HasKey(document => document.Id);
                ConfigureGuid(entity.Property(document => document.Id));
            });

            modelBuilder.Entity<TextGuidDocumentRevision>(entity =>
            {
                entity.ToTable("TextGuidDocumentRevisions");
                entity.HasKey(revision => revision.Id);
                ConfigureGuid(entity.Property(revision => revision.DocumentId));
                entity.HasIndex(revision => revision.DocumentId);
                entity
                    .HasOne(revision => revision.Document)
                    .WithMany(document => document.Revisions)
                    .HasForeignKey(revision => revision.DocumentId)
                    .HasConstraintName(ForeignKeyName)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }

        private void ConfigureGuid(
            PropertyBuilder<Guid> property
        )
        {
            if (UseNativeChar36)
            {
                property.HasMySqlGuidFormat(MySqlGuidFormat.Char36);

                return;
            }

            property
                .HasConversion<string>()
                .HasColumnType("varchar(36)")
                .HasMaxLength(36)
                .IsUnicode(false);
        }
    }

    private sealed class ConvertedGuidContext(DbContextOptions<ConvertedGuidContext> options)
        : GuidMigrationContext(options)
    {
        /// <inheritdoc />
        protected override bool UseNativeChar36 => false;
    }

    private sealed class NativeChar36GuidContext(DbContextOptions<NativeChar36GuidContext> options)
        : GuidMigrationContext(options)
    {
        /// <inheritdoc />
        protected override bool UseNativeChar36 => true;
    }

    private sealed class TextGuidDocument
    {
        /// <summary>
        /// Gets or sets the id.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Gets or sets the revisions.
        /// </summary>
        public ICollection<TextGuidDocumentRevision> Revisions { get; set; } = [];
    }

    private sealed class TextGuidDocumentRevision
    {
        /// <summary>
        /// Gets or sets the id.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the document id.
        /// </summary>
        public Guid DocumentId { get; set; }

        /// <summary>
        /// Gets or sets the document.
        /// </summary>
        public TextGuidDocument Document { get; set; } = null!;
    }
}
