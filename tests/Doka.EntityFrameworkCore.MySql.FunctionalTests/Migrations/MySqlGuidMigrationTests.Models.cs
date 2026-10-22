using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Contains the models responsibility for MySqlGuidMigrationTests.</summary>
public sealed partial class MySqlGuidMigrationTests
{
    private abstract class GuidMigrationContext(DbContextOptions options) : DbContext(options)
    {
        protected abstract MySqlGuidFormat? NativeFormat { get; }

        protected virtual bool UseApplicationBinaryConverter => false;

        protected virtual int DescriptionLength => 64;

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
                entity
                    .Property(revision => revision.Description)
                    .HasMaxLength(DescriptionLength);
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
            if (NativeFormat is { } format)
            {
                property.HasMySqlGuidFormat(format);

                return;
            }

            if (UseApplicationBinaryConverter)
            {
                property
                    .HasConversion(
                        guid => guid.ToByteArray(),
                        bytes => new Guid(bytes))
                    .HasColumnType("binary(16)")
                    .HasMaxLength(16)
                    .IsFixedLength();

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
        protected override MySqlGuidFormat? NativeFormat => null;
    }

    private sealed class ConvertedBinaryGuidContext(DbContextOptions<ConvertedBinaryGuidContext> options)
        : GuidMigrationContext(options)
    {
        protected override MySqlGuidFormat? NativeFormat => null;

        protected override bool UseApplicationBinaryConverter => true;
    }

    private sealed class NativeChar36GuidContext(DbContextOptions<NativeChar36GuidContext> options)
        : GuidMigrationContext(options)
    {
        protected override MySqlGuidFormat? NativeFormat => MySqlGuidFormat.Char36;
    }

    private sealed class NativeBinary16GuidContext(DbContextOptions<NativeBinary16GuidContext> options)
        : GuidMigrationContext(options)
    {
        protected override MySqlGuidFormat? NativeFormat => MySqlGuidFormat.Binary16;
    }

    private sealed class ShortDescriptionGuidContext(DbContextOptions<ShortDescriptionGuidContext> options)
        : GuidMigrationContext(options)
    {
        protected override MySqlGuidFormat? NativeFormat => MySqlGuidFormat.Char36;
    }

    private sealed class LongDescriptionGuidContext(DbContextOptions<LongDescriptionGuidContext> options)
        : GuidMigrationContext(options)
    {
        protected override MySqlGuidFormat? NativeFormat => MySqlGuidFormat.Char36;

        protected override int DescriptionLength => 128;
    }

    private abstract class RelationshipEvolutionContext(DbContextOptions options) : DbContext(options)
    {
        protected abstract bool IncludesRelationship { get; }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<EvolutionCustomer>(entity =>
            {
                entity.ToTable("GuidEvolutionCustomers");
                entity.HasKey(item => item.Id);

                if (!IncludesRelationship)
                {
                    entity.Ignore(item => item.Shipments);
                }
            });

            modelBuilder.Entity<EvolutionShipment>(entity =>
            {
                entity.ToTable("GuidEvolutionShipments");
                entity.HasKey(item => item.Id);

                if (!IncludesRelationship)
                {
                    entity.Ignore(item => item.CustomerId);
                    entity.Ignore(item => item.Customer);

                    return;
                }

                entity
                    .HasOne(item => item.Customer)
                    .WithMany(item => item.Shipments)
                    .HasForeignKey(item => item.CustomerId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }

    private sealed class RelationshipEvolutionSourceContext(
        DbContextOptions<RelationshipEvolutionSourceContext> options
    ) : RelationshipEvolutionContext(options)
    {
        protected override bool IncludesRelationship => false;
    }

    private sealed class RelationshipEvolutionTargetContext(
        DbContextOptions<RelationshipEvolutionTargetContext> options
    ) : RelationshipEvolutionContext(options)
    {
        protected override bool IncludesRelationship => true;
    }

    private abstract class RequiredBackfillContext(DbContextOptions options) : DbContext(options)
    {
        protected abstract bool AssigneeRequired { get; }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.SharedTypeEntity<Dictionary<string, object>>("GuidAssignee", entity =>
            {
                entity.ToTable("GuidAssignees");
                entity.IndexerProperty<Guid>("Id");
                entity.HasKey("Id");
            });

            modelBuilder.SharedTypeEntity<Dictionary<string, object>>("GuidWorkItem", entity =>
            {
                entity.ToTable("GuidWorkItems");
                entity.IndexerProperty<int>("Id");
                entity.HasKey("Id");

                if (AssigneeRequired)
                {
                    entity.IndexerProperty<Guid>("AssigneeId");
                }
                else
                {
                    entity.IndexerProperty<Guid?>("AssigneeId");
                }

                entity
                    .HasOne("GuidAssignee", null)
                    .WithMany()
                    .HasForeignKey("AssigneeId")
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }

    private sealed class RequiredBackfillSourceContext(
        DbContextOptions<RequiredBackfillSourceContext> options
    ) : RequiredBackfillContext(options)
    {
        protected override bool AssigneeRequired => false;
    }

    private sealed class RequiredBackfillTargetContext(
        DbContextOptions<RequiredBackfillTargetContext> options
    ) : RequiredBackfillContext(options)
    {
        protected override bool AssigneeRequired => true;
    }

    private abstract class BinaryOverrideContext(DbContextOptions options) : DbContext(options)
    {
        protected abstract bool IncludesReference { get; }

        protected virtual bool ReferenceRequired => false;

        protected virtual bool HasReferenceDefault => false;

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.SharedTypeEntity<Dictionary<string, object>>("BinaryOverrideRecord", entity =>
            {
                entity.ToTable("BinaryOverrideRecords");
                entity.IndexerProperty<int>("Id");
                entity.HasKey("Id");

                if (!IncludesReference)
                {
                    return;
                }

                PropertyBuilder property = ReferenceRequired
                    ? entity.IndexerProperty<Guid>("ReferenceId")
                    : entity.IndexerProperty<Guid?>("ReferenceId");

                property.HasMySqlGuidFormat(MySqlGuidFormat.Binary16);

                if (HasReferenceDefault)
                {
                    property.HasDefaultValue(Guid.Parse(BinaryOverrideDefault));
                }
            });
        }
    }

    private sealed class BinaryOverrideEmptyContext(
        DbContextOptions<BinaryOverrideEmptyContext> options
    ) : BinaryOverrideContext(options)
    {
        protected override bool IncludesReference => false;
    }

    private sealed class BinaryOverrideNullableContext(
        DbContextOptions<BinaryOverrideNullableContext> options
    ) : BinaryOverrideContext(options)
    {
        protected override bool IncludesReference => true;
    }

    private sealed class BinaryOverrideRequiredContext(
        DbContextOptions<BinaryOverrideRequiredContext> options
    ) : BinaryOverrideContext(options)
    {
        protected override bool IncludesReference => true;

        protected override bool ReferenceRequired => true;
    }

    private sealed class BinaryOverrideDefaultedContext(
        DbContextOptions<BinaryOverrideDefaultedContext> options
    ) : BinaryOverrideContext(options)
    {
        protected override bool IncludesReference => true;

        protected override bool ReferenceRequired => true;

        protected override bool HasReferenceDefault => true;
    }

    private sealed class EvolutionCustomer
    {
        public Guid Id { get; set; }

        public ICollection<EvolutionShipment> Shipments { get; } = [];
    }

    private sealed class EvolutionShipment
    {
        public int Id { get; set; }

        public Guid CustomerId { get; set; }

        public EvolutionCustomer Customer { get; set; } = null!;
    }

    private sealed class FixedMigrationsModelDiffer(
        params MigrationOperation[] operations
    ) : IMigrationsModelDiffer
    {
        public bool HasDifferences(
            IRelationalModel? source,
            IRelationalModel? target
        ) => operations.Length > 0;

        public IReadOnlyList<MigrationOperation> GetDifferences(
            IRelationalModel? source,
            IRelationalModel? target
        ) => operations;
    }

    private sealed class TextGuidDocument
    {
        public Guid Id { get; set; }

        public ICollection<TextGuidDocumentRevision> Revisions { get; } = [];
    }

    private sealed class TextGuidDocumentRevision
    {
        public int Id { get; set; }

        public Guid DocumentId { get; set; }

        public string Description { get; set; } = string.Empty;

        public TextGuidDocument Document { get; set; } = null!;
    }
}
