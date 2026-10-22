namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Test context for generated provider-native MariaDB UUID migration models.
/// </summary>
public sealed class MariaDbUuidDesignContext : DbContext
{
    /// <summary>
    /// Creates the native UUID design context.
    /// </summary>
    public MariaDbUuidDesignContext(
        DbContextOptions<MariaDbUuidDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<MariaDbUuidDesignRecord>(entity =>
        {
            entity.ToTable("MariaDbUuidDesignRecords");
            entity.HasKey(record => record.Id);
            entity.HasData(new MariaDbUuidDesignRecord
            {
                Id = new Guid("bf1da273-beed-4197-ab57-4cf8395244d4"),
                OptionalId = null,
            });
        });
    }
}

/// <summary>
/// Seeded entity used to verify native UUID design-time model values.
/// </summary>
public sealed class MariaDbUuidDesignRecord
{
    /// <summary>
    /// Gets or sets the native UUID key.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets an optional native UUID value.
    /// </summary>
    public Guid? OptionalId { get; set; }
}

/// <summary>
/// Test context for generated provider-native Char36 migration models.
/// </summary>
public sealed class Char36DesignContext : DbContext
{
    /// <summary>
    /// Creates the test context.
    /// </summary>
    public Char36DesignContext(
        DbContextOptions<Char36DesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<Char36DesignDocument>(entity =>
        {
            entity.ToTable("Char36DesignDocuments");
            entity.HasKey(document => document.Id);
            entity
                .Property(document => document.Id)
                .HasMySqlGuidFormat(MySqlGuidFormat.Char36)
                .UseMySqlClientGuidValueGeneration();
        });

        modelBuilder.Entity<Char36DesignRevision>(entity =>
        {
            entity.ToTable("Char36DesignRevisions");
            entity.HasKey(revision => revision.Id);
            entity
                .Property(revision => revision.DocumentId)
                .HasMySqlGuidFormat(MySqlGuidFormat.Char36);
            entity
                .HasOne(revision => revision.Document)
                .WithMany(document => document.Revisions)
                .HasForeignKey(revision => revision.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

/// <summary>
/// Principal entity used by the generated Char36 migration model.
/// </summary>
public sealed class Char36DesignDocument
{
    /// <summary>
    /// Gets or sets the client-generated key.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets the dependent revisions.
    /// </summary>
    public ICollection<Char36DesignRevision> Revisions { get; } = [];
}

/// <summary>
/// Dependent entity used by the generated Char36 migration model.
/// </summary>
public sealed class Char36DesignRevision
{
    /// <summary>
    /// Gets or sets the revision key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the principal Guid key.
    /// </summary>
    public Guid DocumentId { get; set; }

    /// <summary>
    /// Gets or sets the principal navigation.
    /// </summary>
    public Char36DesignDocument Document { get; set; } = null!;
}

/// <summary>
/// Test context for generated relationship models using a context-level Char36 default.
/// </summary>
public sealed class DefaultChar36RelationshipDesignContext : DbContext
{
    /// <summary>
    /// Creates the relationship design context.
    /// </summary>
    public DefaultChar36RelationshipDesignContext(
        DbContextOptions<DefaultChar36RelationshipDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<DefaultChar36RelationshipARoot>(entity => entity.HasKey(item => item.Id));

        modelBuilder.Entity<DefaultChar36RelationshipBLeft>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity
                .HasOne(item => item.Root)
                .WithOne()
                .HasForeignKey<DefaultChar36RelationshipBLeft>(item => item.Id);
        });

        modelBuilder.Entity<DefaultChar36RelationshipCRight>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity
                .HasOne(item => item.Root)
                .WithOne()
                .HasForeignKey<DefaultChar36RelationshipCRight>(item => item.Id);
        });

        modelBuilder.Entity<DefaultChar36RelationshipZLeaf>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity
                .HasOne(item => item.Left)
                .WithMany()
                .HasForeignKey(item => item.ReferenceId);
            entity
                .HasOne(item => item.Right)
                .WithMany()
                .HasForeignKey(item => item.ReferenceId);
        });

        modelBuilder.Entity<DefaultChar36RelationshipZNullableLeaf>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity
                .HasOne(item => item.Left)
                .WithMany()
                .HasForeignKey(item => item.OptionalReferenceId);
            entity
                .HasOne(item => item.Right)
                .WithMany()
                .HasForeignKey(item => item.OptionalReferenceId);
        });
    }
}

/// <summary>
/// Root entity for the generated context-level Char36 relationship model.
/// </summary>
public sealed class DefaultChar36RelationshipARoot
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public Guid Id { get; set; }
}

/// <summary>
/// Left relationship branch for the generated context-level Char36 model.
/// </summary>
public sealed class DefaultChar36RelationshipBLeft
{
    /// <summary>
    /// Gets or sets the shared primary and foreign key.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the root navigation.
    /// </summary>
    public DefaultChar36RelationshipARoot Root { get; set; } = null!;
}

/// <summary>
/// Right relationship branch for the generated context-level Char36 model.
/// </summary>
public sealed class DefaultChar36RelationshipCRight
{
    /// <summary>
    /// Gets or sets the shared primary and foreign key.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the root navigation.
    /// </summary>
    public DefaultChar36RelationshipARoot Root { get; set; } = null!;
}

/// <summary>
/// Leaf entity whose GUID participates in both relationship branches.
/// </summary>
public sealed class DefaultChar36RelationshipZLeaf
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the GUID shared by both foreign keys.
    /// </summary>
    public Guid ReferenceId { get; set; }

    /// <summary>
    /// Gets or sets the left branch navigation.
    /// </summary>
    public DefaultChar36RelationshipBLeft Left { get; set; } = null!;

    /// <summary>
    /// Gets or sets the right branch navigation.
    /// </summary>
    public DefaultChar36RelationshipCRight Right { get; set; } = null!;
}

/// <summary>
/// Optional leaf entity whose nullable GUID participates in both relationship branches.
/// </summary>
public sealed class DefaultChar36RelationshipZNullableLeaf
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the optional GUID shared by both foreign keys.
    /// </summary>
    public Guid? OptionalReferenceId { get; set; }

    /// <summary>
    /// Gets or sets the optional left branch navigation.
    /// </summary>
    public DefaultChar36RelationshipBLeft? Left { get; set; }

    /// <summary>
    /// Gets or sets the optional right branch navigation.
    /// </summary>
    public DefaultChar36RelationshipCRight? Right { get; set; }
}

/// <summary>
/// Test context for mixed default and property-level Guid formats.
/// </summary>
public sealed class MixedGuidDesignContext : DbContext
{
    /// <summary>
    /// Creates the mixed Guid design context.
    /// </summary>
    public MixedGuidDesignContext(
        DbContextOptions<MixedGuidDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<MixedGuidDesignRecord>(entity =>
        {
            entity.ToTable("MixedGuidDesignRecords");
            entity.HasKey(record => record.Id);
            entity
                .Property(record => record.BinaryReference)
                .HasMySqlGuidFormat(MySqlGuidFormat.Binary16);
            entity
                .HasIndex(record => new
                {
                    record.Name,
                    record.BinaryReference,
                })
                .HasDatabaseName("IX_MixedGuidDesignRecords_Name_BinaryReference")
                .HasPrefixLength(16, 0);
        });
    }
}

/// <summary>
/// Entity used by the mixed Guid design model.
/// </summary>
public sealed class MixedGuidDesignRecord
{
    /// <summary>
    /// Gets or sets the Char36 default key.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the explicitly Binary16 value.
    /// </summary>
    public Guid BinaryReference { get; set; }

    /// <summary>
    /// Gets or sets the indexed text value.
    /// </summary>
    public string Name { get; set; } = string.Empty;
}
