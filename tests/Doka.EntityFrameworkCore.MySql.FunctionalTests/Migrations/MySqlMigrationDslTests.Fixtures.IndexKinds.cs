namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMigrationDslTests
{
    private sealed class PlainTextIndexContext : DbContext
    {
        /// <summary>
        /// Initializes the plain text index context fixture.
        /// </summary>
        public PlainTextIndexContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureTextIndex(modelBuilder, fullText: false);
    }

    private sealed class FullTextIndexContext : DbContext
    {
        /// <summary>
        /// Initializes the full text index context fixture.
        /// </summary>
        public FullTextIndexContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureTextIndex(modelBuilder, fullText: true);
    }

    private sealed class PlainSpatialIndexContext : DbContext
    {
        /// <summary>
        /// Initializes the plain spatial index context fixture.
        /// </summary>
        public PlainSpatialIndexContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureSpatialIndex(modelBuilder, spatial: false);
    }

    private sealed class SpatialIndexContext : DbContext
    {
        /// <summary>
        /// Initializes the spatial index context fixture.
        /// </summary>
        public SpatialIndexContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureSpatialIndex(modelBuilder, spatial: true);
    }

    private static void ConfigureTextIndex(
        ModelBuilder modelBuilder,
        bool fullText
    )
    {
        var index = modelBuilder
            .Entity<TextIndexEntity>()
            .ToTable(TextIndexContract.TableName)
            .HasIndex(entity => entity.Body)
            .HasDatabaseName(TextIndexContract.IndexName);

        modelBuilder
            .Entity<TextIndexEntity>()
            .Property(entity => entity.Body)
            .HasMaxLength(256);

        if (fullText)
        {
            index.IsFullText();
        }
    }

    private static void ConfigureSpatialIndex(
        ModelBuilder modelBuilder,
        bool spatial
    )
    {
        var index = modelBuilder
            .Entity<SpatialIndexEntity>()
            .ToTable(SpatialIndexContract.TableName)
            .HasIndex(entity => entity.Location)
            .HasDatabaseName(SpatialIndexContract.IndexName);

        modelBuilder
            .Entity<SpatialIndexEntity>()
            .Property(entity => entity.Location)
            .HasColumnType("point")
            .HasSrid(0);

        if (spatial)
        {
            index.IsSpatial();
        }
    }

    private sealed class TextIndexEntity
    {
        /// <summary>
        /// Gets or sets the entity key.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the body.
        /// </summary>
        public string Body { get; set; } = string.Empty;
    }

    private sealed class SpatialIndexEntity
    {
        /// <summary>
        /// Gets or sets the entity key.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Initializes the point fixture.
        /// </summary>
        public Point Location { get; set; } = new(0, 0);
    }

    private static class TextIndexContract
    {
        /// <summary>
        /// The table name used by the migration fixture.
        /// </summary>
        public const string TableName = "TextIndexRecords";
        /// <summary>
        /// The index name used by the migration fixture.
        /// </summary>
        public const string IndexName = "IX_TextIndexRecords_Body";
    }

    private static class SpatialIndexContract
    {
        /// <summary>
        /// The table name used by the migration fixture.
        /// </summary>
        public const string TableName = "SpatialIndexRecords";
        /// <summary>
        /// The index name used by the migration fixture.
        /// </summary>
        public const string IndexName = "IX_SpatialIndexRecords_Location";
    }
}
