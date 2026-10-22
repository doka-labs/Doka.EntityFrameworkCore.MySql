namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMigrationDslTests
{
    private static DbContext CreateIndexPrefixContext(
        IndexPrefixTransition transition,
        bool source
    ) => (transition, source) switch
    {
        (IndexPrefixTransition.Change, true) =>
            new SourceIndexPrefixContext(CreateOptions<SourceIndexPrefixContext>()),
        (IndexPrefixTransition.Change, false) or (IndexPrefixTransition.Remove, true) =>
            new TargetIndexPrefixContext(CreateOptions<TargetIndexPrefixContext>()),
        (IndexPrefixTransition.Add, true) or (IndexPrefixTransition.Remove, false) =>
            new NoIndexPrefixContext(CreateOptions<NoIndexPrefixContext>()),
        (IndexPrefixTransition.Add, false) =>
            new TargetIndexPrefixContext(CreateOptions<TargetIndexPrefixContext>()),
        _ => throw new ArgumentOutOfRangeException(nameof(transition), transition, null),
    };

    private sealed class SourceIndexPrefixContext : DbContext
    {
        /// <summary>
        /// Initializes the source index prefix context fixture.
        /// </summary>
        public SourceIndexPrefixContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureIndexPrefix(modelBuilder, IndexPrefixContract.SourcePrefixLengths);
    }

    private sealed class TargetIndexPrefixContext : DbContext
    {
        /// <summary>
        /// Initializes the target index prefix context fixture.
        /// </summary>
        public TargetIndexPrefixContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureIndexPrefix(modelBuilder, IndexPrefixContract.TargetPrefixLengths);
    }

    private sealed class NoIndexPrefixContext : DbContext
    {
        /// <summary>
        /// Initializes the no index prefix context fixture.
        /// </summary>
        public NoIndexPrefixContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureIndexPrefix(modelBuilder, prefixLengths: null);
    }

    private sealed class RenamedIndexPrefixContext : DbContext
    {
        /// <summary>
        /// Initializes the renamed index prefix context fixture.
        /// </summary>
        public RenamedIndexPrefixContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureIndexPrefix(
            modelBuilder,
            IndexPrefixContract.TargetPrefixLengths,
            indexName: IndexPrefixContract.RenamedIndexName);
    }

    private sealed class RenamedTableIndexPrefixContext : DbContext
    {
        /// <summary>
        /// Initializes the renamed table index prefix context fixture.
        /// </summary>
        public RenamedTableIndexPrefixContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureIndexPrefix(
            modelBuilder,
            IndexPrefixContract.TargetPrefixLengths,
            tableName: IndexPrefixContract.RenamedTableName);
    }

    private sealed class RenamedTableAndIndexPrefixContext : DbContext
    {
        /// <summary>
        /// Initializes the renamed table and index prefix context fixture.
        /// </summary>
        public RenamedTableAndIndexPrefixContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureIndexPrefix(
            modelBuilder,
            IndexPrefixContract.TargetPrefixLengths,
            IndexPrefixContract.RenamedTableName,
            IndexPrefixContract.RenamedIndexName);
    }

    private sealed class RenamedIndexSamePrefixContext : DbContext
    {
        /// <summary>
        /// Initializes the renamed index same prefix context fixture.
        /// </summary>
        public RenamedIndexSamePrefixContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureIndexPrefix(
            modelBuilder,
            IndexPrefixContract.SourcePrefixLengths,
            indexName: IndexPrefixContract.RenamedIndexName);
    }

    private sealed class RenamedTableSamePrefixContext : DbContext
    {
        /// <summary>
        /// Initializes the renamed table same prefix context fixture.
        /// </summary>
        public RenamedTableSamePrefixContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureIndexPrefix(
            modelBuilder,
            IndexPrefixContract.SourcePrefixLengths,
            tableName: IndexPrefixContract.RenamedTableName);
    }

    private static void ConfigureIndexPrefix(
        ModelBuilder modelBuilder,
        int[]? prefixLengths,
        string tableName = IndexPrefixContract.TableName,
        string indexName = IndexPrefixContract.IndexName
    )
    {
        var index = modelBuilder
            .Entity<IndexPrefixEntity>()
            .ToTable(tableName)
            .HasIndex(entity => new
            {
                entity.TenantId,
                entity.Code,
            })
            .HasDatabaseName(indexName);

        if (prefixLengths is not null)
        {
            index.HasPrefixLength(prefixLengths);
        }
    }

    private sealed class IndexPrefixEntity
    {
        /// <summary>
        /// Gets or sets the entity key.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the tenant id.
        /// </summary>
        public int TenantId { get; set; }

        /// <summary>
        /// Gets or sets the code.
        /// </summary>
        public string Code { get; set; } = string.Empty;
    }

    private static class IndexPrefixContract
    {
        /// <summary>
        /// The table name used by the migration fixture.
        /// </summary>
        public const string TableName = "IndexPrefixRecords";
        /// <summary>
        /// The renamed table name used by the migration fixture.
        /// </summary>
        public const string RenamedTableName = "RenamedIndexPrefixRecords";
        /// <summary>
        /// The index name used by the migration fixture.
        /// </summary>
        public const string IndexName = "IX_IndexPrefixRecords_TenantId_Code";
        /// <summary>
        /// The renamed index name used by the migration fixture.
        /// </summary>
        public const string RenamedIndexName = "IX_IndexPrefixRecords_TenantId_Code_Renamed";
        /// <summary>
        /// Gets or sets the readonly.
        /// </summary>
        public static readonly int[] SourcePrefixLengths = [0, 24];
        /// <summary>
        /// Gets or sets the readonly.
        /// </summary>
        public static readonly int[] TargetPrefixLengths = [0, 48];
    }

    /// <summary>
    /// Identifies changes to the provider-owned indexed-prefix metadata.
    /// </summary>
    public enum IndexPrefixTransition
    {
        /// <summary>Changes an existing prefix length.</summary>
        Change,
        /// <summary>Adds a bounded prefix to a previously complete value.</summary>
        Add,
        /// <summary>Removes the bounded prefix.</summary>
        Remove,
    }
}
