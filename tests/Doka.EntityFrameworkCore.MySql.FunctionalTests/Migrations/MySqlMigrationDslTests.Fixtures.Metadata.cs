namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMigrationDslTests
{
    private sealed class EmptyMigrationDslContext : DbContext
    {
        /// <summary>
        /// Initializes the empty migration dsl context fixture.
        /// </summary>
        public EmptyMigrationDslContext(
            DbContextOptions options
        ) : base(options) { }
    }

    private sealed class MigrationDslContext : DbContext
    {
        /// <summary>
        /// Initializes the migration dsl context fixture.
        /// </summary>
        public MigrationDslContext(
            DbContextOptions options
        ) : base(options) { }

        /// <summary>
        /// Initializes the db set fixture.
        /// </summary>
        public DbSet<MigrationDslEntity> MigrationDslEntities => Set<MigrationDslEntity>();

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.HasCharSet("utf8mb4");

            modelBuilder.Entity<MigrationDslEntity>(entity =>
            {
                entity.HasCharSet("utf8mb4");
                entity.UseStorageEngine("InnoDB");
                entity
                    .Property(item => item.ExternalId)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Char36);
                entity
                    .HasIndex(item => new
                    {
                        item.Name,
                        item.Code,
                    })
                    .HasDatabaseName("IX_MigrationDsl_Name_Code")
                    .HasPrefixLength(s_indexPrefixLengths)
                    .IsDescending(s_mixedIndexDirections);
                entity
                    .HasIndex(item => item.Body)
                    .HasDatabaseName("IX_MigrationDsl_Body")
                    .IsFullText();
                entity
                    .Property(item => item.Location)
                    .HasColumnType("point");
                entity
                    .HasIndex(item => item.Location)
                    .HasDatabaseName("IX_MigrationDsl_Location")
                    .IsSpatial();
            });
        }
    }

    private sealed class MigrationDslEntity
    {
        /// <summary>
        /// Gets or sets the entity key.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the external id.
        /// </summary>
        public Guid ExternalId { get; set; }

        /// <summary>
        /// Gets or sets the name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the code.
        /// </summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the body.
        /// </summary>
        public string Body { get; set; } = string.Empty;

        /// <summary>
        /// Initializes the point fixture.
        /// </summary>
        public Point Location { get; set; } = new(0, 0);
    }
}
