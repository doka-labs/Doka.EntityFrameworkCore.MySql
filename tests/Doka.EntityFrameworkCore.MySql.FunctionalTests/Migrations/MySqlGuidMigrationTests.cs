using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies migration contracts for provider-native Guid storage transitions.
/// </summary>
public sealed class MySqlGuidMigrationTests
{
    private const string ForeignKeyName = "FK_TextGuidDocumentRevisions_TextGuidDocuments_DocumentId";

    /// <summary>
    /// A store-type transition on both sides of an existing foreign key owns a
    /// symmetric drop/alter/recreate lifecycle in both migration directions.
    /// </summary>
    [Fact]
    public void Char36_transition_orders_foreign_key_around_both_column_alters()
    {
        using var converted = new ConvertedGuidContext(CreateOptions<ConvertedGuidContext>());
        using var native = new NativeChar36GuidContext(CreateOptions<NativeChar36GuidContext>());

        AssertTransition(converted, native, "varchar(36)", "char(36)");
        AssertTransition(native, converted, "char(36)", "varchar(36)");
    }

    /// <summary>
    /// A store-type change outside a foreign-key shape must not rebuild an unrelated
    /// relationship merely because both operations affect the same table.
    /// </summary>
    [Fact]
    public void Unrelated_store_type_change_does_not_rebuild_foreign_key()
    {
        using var source = new ShortDescriptionGuidContext(CreateOptions<ShortDescriptionGuidContext>());
        using var target = new LongDescriptionGuidContext(CreateOptions<LongDescriptionGuidContext>());

        var operations = target
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(
                source.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
                target.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        var alterColumn = Assert.Single(operations.OfType<AlterColumnOperation>());
        Assert.Equal(nameof(TextGuidDocumentRevision.Description), alterColumn.Name);
        Assert.Empty(operations.OfType<DropForeignKeyOperation>());
        Assert.Empty(operations.OfType<AddForeignKeyOperation>());
    }

    private static void AssertTransition(
        DbContext source,
        DbContext target,
        string oldStoreType,
        string newStoreType
    )
    {
        var operations = target
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(
                source
                    .GetService<IDesignTimeModel>()
                    .Model
                    .GetRelationalModel(),
                target
                    .GetService<IDesignTimeModel>()
                    .Model
                    .GetRelationalModel())
            .ToList();

        var dropForeignKey = Assert.Single(operations.OfType<DropForeignKeyOperation>());
        var alterColumns = operations
            .OfType<AlterColumnOperation>()
            .ToArray();

        var addForeignKey = Assert.Single(operations.OfType<AddForeignKeyOperation>());

        Assert.Equal(ForeignKeyName, dropForeignKey.Name);
        Assert.Equal("TextGuidDocumentRevisions", dropForeignKey.Table);
        Assert.Collection(
            alterColumns.OrderBy(operation => operation.Table, StringComparer.Ordinal),
            operation => AssertColumnTransition(
                operation,
                "TextGuidDocumentRevisions",
                "DocumentId",
                oldStoreType,
                newStoreType),
            operation => AssertColumnTransition(operation, "TextGuidDocuments", "Id", oldStoreType, newStoreType));
        Assert.Equal(ForeignKeyName, addForeignKey.Name);
        Assert.Equal("TextGuidDocumentRevisions", addForeignKey.Table);
        Assert.Equal(["DocumentId"], addForeignKey.Columns);
        Assert.Equal("TextGuidDocuments", addForeignKey.PrincipalTable);
        Assert.NotNull(addForeignKey.PrincipalColumns);
        Assert.Equal(["Id"], addForeignKey.PrincipalColumns);
        Assert.Equal(ReferentialAction.Cascade, addForeignKey.OnDelete);
        Assert.True(operations.IndexOf(dropForeignKey) < operations.IndexOf(alterColumns[0]));
        Assert.True(operations.IndexOf(dropForeignKey) < operations.IndexOf(alterColumns[1]));
        Assert.True(operations.IndexOf(addForeignKey) > operations.IndexOf(alterColumns[0]));
        Assert.True(operations.IndexOf(addForeignKey) > operations.IndexOf(alterColumns[1]));
        Assert.Empty(operations.OfType<DropIndexOperation>());
        Assert.Empty(operations.OfType<CreateIndexOperation>());
    }

    private static void AssertColumnTransition(
        AlterColumnOperation operation,
        string table,
        string column,
        string oldStoreType,
        string newStoreType
    )
    {
        Assert.Equal(table, operation.Table);
        Assert.Equal(column, operation.Name);
        Assert.Equal(newStoreType, operation.ColumnType);
        Assert.Equal(oldStoreType, operation.OldColumn.ColumnType);
    }

    private static DbContextOptions<TContext> CreateOptions<TContext>()
        where TContext : DbContext => new DbContextOptionsBuilder<TContext>().UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MariaDb(new Version(11, 8, 0)))
        .Options;

    private abstract class GuidMigrationContext(DbContextOptions options) : DbContext(options)
    {
        protected abstract bool UseNativeChar36 { get; }

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
        protected override bool UseNativeChar36 => false;
    }

    private sealed class NativeChar36GuidContext(DbContextOptions<NativeChar36GuidContext> options)
        : GuidMigrationContext(options)
    {
        protected override bool UseNativeChar36 => true;
    }

    private sealed class ShortDescriptionGuidContext(DbContextOptions<ShortDescriptionGuidContext> options)
        : GuidMigrationContext(options)
    {
        protected override bool UseNativeChar36 => true;
    }

    private sealed class LongDescriptionGuidContext(DbContextOptions<LongDescriptionGuidContext> options)
        : GuidMigrationContext(options)
    {
        protected override bool UseNativeChar36 => true;

        protected override int DescriptionLength => 128;
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
