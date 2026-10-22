namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMigrationDslTests
{
    private abstract class DatabaseOptionsContext : DbContext
    {
        protected DatabaseOptionsContext(
            DbContextOptions options
        ) : base(options) { }

        protected abstract string? DatabaseCharSet { get; }

        protected abstract string? DatabaseCollation { get; }

        protected virtual string? TableCollation => null;

        protected virtual string? TableCharSet => null;

        protected virtual string? TableStorageEngine => null;

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            if (DatabaseCharSet is not null)
            {
                modelBuilder.HasCharSet(DatabaseCharSet);
            }

            if (DatabaseCollation is not null)
            {
                modelBuilder.UseCollation(DatabaseCollation);
            }

            var entity = modelBuilder.Entity<DatabaseOptionEntity>();
            entity.ToTable("DatabaseOptionEntities");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(64);

            if (TableCollation is not null)
            {
                entity.Metadata.SetAnnotation(RelationalAnnotationNames.Collation, TableCollation);
            }

            if (TableCharSet is not null)
            {
                entity.HasCharSet(TableCharSet);
            }

            if (TableStorageEngine is not null)
            {
                entity.UseStorageEngine(TableStorageEngine);
            }
        }
    }

    private sealed class Utf8DatabaseOptionsContext : DatabaseOptionsContext
    {
        /// <summary>
        /// Initializes the utf8 database options context fixture.
        /// </summary>
        public Utf8DatabaseOptionsContext(
            DbContextOptions options
        ) : base(options) { }

        /// <summary>
        /// Gets or sets the database char set.
        /// </summary>
        protected override string DatabaseCharSet => "utf8mb4";

        /// <summary>
        /// Gets or sets the database collation.
        /// </summary>
        protected override string DatabaseCollation => "utf8mb4_unicode_ci";
    }

    private sealed class Latin1DatabaseOptionsContext : DatabaseOptionsContext
    {
        /// <summary>
        /// Initializes the latin1 database options context fixture.
        /// </summary>
        public Latin1DatabaseOptionsContext(
            DbContextOptions options
        ) : base(options) { }

        /// <summary>
        /// Gets or sets the database char set.
        /// </summary>
        protected override string DatabaseCharSet => "latin1";

        /// <summary>
        /// Gets or sets the database collation.
        /// </summary>
        protected override string DatabaseCollation => "latin1_swedish_ci";
    }

    private sealed class UnicodeCollationOnlyContext : DatabaseOptionsContext
    {
        /// <summary>
        /// Initializes the Unicode collation only context fixture.
        /// </summary>
        public UnicodeCollationOnlyContext(
            DbContextOptions options
        ) : base(options) { }

        /// <summary>
        /// Gets or sets the database char set.
        /// </summary>
        protected override string? DatabaseCharSet => null;

        /// <summary>
        /// Gets or sets the database collation.
        /// </summary>
        protected override string DatabaseCollation => "utf8mb4_unicode_ci";
    }

    private sealed class BinaryCollationOnlyContext : DatabaseOptionsContext
    {
        /// <summary>
        /// Initializes the binary collation only context fixture.
        /// </summary>
        public BinaryCollationOnlyContext(
            DbContextOptions options
        ) : base(options) { }

        /// <summary>
        /// Gets or sets the database char set.
        /// </summary>
        protected override string? DatabaseCharSet => null;

        /// <summary>
        /// Gets or sets the database collation.
        /// </summary>
        protected override string DatabaseCollation => "utf8mb4_bin";
    }

    private sealed class DatabaseOptionsUnsetContext : DatabaseOptionsContext
    {
        /// <summary>
        /// Initializes the database options unset context fixture.
        /// </summary>
        public DatabaseOptionsUnsetContext(
            DbContextOptions options
        ) : base(options) { }

        /// <summary>
        /// Gets or sets the database char set.
        /// </summary>
        protected override string? DatabaseCharSet => null;

        /// <summary>
        /// Gets or sets the database collation.
        /// </summary>
        protected override string? DatabaseCollation => null;
    }

    private sealed class MatchingTableCollationContext : Utf8DatabaseOptionsContextBase
    {
        /// <summary>
        /// Initializes the matching table collation context fixture.
        /// </summary>
        public MatchingTableCollationContext(
            DbContextOptions options
        ) : base(options) { }

        /// <summary>
        /// Gets or sets the table collation.
        /// </summary>
        protected override string TableCollation => "utf8mb4_unicode_ci";
    }

    private sealed class DifferentTableCollationContext : Utf8DatabaseOptionsContextBase
    {
        /// <summary>
        /// Initializes the different table collation context fixture.
        /// </summary>
        public DifferentTableCollationContext(
            DbContextOptions options
        ) : base(options) { }

        /// <summary>
        /// Gets or sets the table collation.
        /// </summary>
        protected override string TableCollation => "utf8mb4_bin";
    }

    private sealed class MyIsamTableOptionsContext : Utf8DatabaseOptionsContextBase
    {
        /// <summary>
        /// Initializes the my isam table options context fixture.
        /// </summary>
        public MyIsamTableOptionsContext(
            DbContextOptions options
        ) : base(options) { }

        /// <summary>
        /// Gets or sets the table storage engine.
        /// </summary>
        protected override string TableStorageEngine => "MyISAM";
    }

    private sealed class Latin1TableOptionsContext : Utf8DatabaseOptionsContextBase
    {
        /// <summary>
        /// Initializes the latin1 table options context fixture.
        /// </summary>
        public Latin1TableOptionsContext(
            DbContextOptions options
        ) : base(options) { }

        /// <summary>
        /// Gets or sets the table char set.
        /// </summary>
        protected override string TableCharSet => "latin1";

        /// <summary>
        /// Gets or sets the table collation.
        /// </summary>
        protected override string TableCollation => "latin1_swedish_ci";
    }

    private sealed class InnoDbTableOptionsContext : Utf8DatabaseOptionsContextBase
    {
        /// <summary>
        /// Initializes the innodb table options context fixture.
        /// </summary>
        public InnoDbTableOptionsContext(
            DbContextOptions options
        ) : base(options) { }

        /// <summary>
        /// Gets or sets the table storage engine.
        /// </summary>
        protected override string TableStorageEngine => "InnoDB";
    }

    private abstract class Utf8DatabaseOptionsContextBase : DatabaseOptionsContext
    {
        protected Utf8DatabaseOptionsContextBase(
            DbContextOptions options
        ) : base(options) { }

        /// <summary>
        /// Gets or sets the database char set.
        /// </summary>
        protected override string DatabaseCharSet => "utf8mb4";

        /// <summary>
        /// Gets or sets the database collation.
        /// </summary>
        protected override string DatabaseCollation => "utf8mb4_unicode_ci";
    }

    private sealed class DatabaseOptionEntity
    {
        /// <summary>
        /// Gets or sets the entity key.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the name.
        /// </summary>
        public string Name { get; set; } = null!;
    }
}
