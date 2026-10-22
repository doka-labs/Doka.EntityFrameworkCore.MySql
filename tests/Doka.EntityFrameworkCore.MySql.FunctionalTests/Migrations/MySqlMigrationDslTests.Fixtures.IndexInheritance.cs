namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMigrationDslTests
{
    private sealed class InheritedIndexContext : DbContext
    {
        /// <summary>
        /// Initializes the inherited index context fixture.
        /// </summary>
        public InheritedIndexContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder
                .Entity<InheritedIndexBase>()
                .ToTable(InheritedIndexContract.BaseTable)
                .HasIndex(entity => new
                {
                    entity.AlternateId,
                    entity.Id,
                })
                .HasDatabaseName(InheritedIndexContract.BaseIndex)
                .HasPrefixLength(16, 0);

            modelBuilder
                .Entity<InheritedIndexDerived>()
                .ToTable(InheritedIndexContract.DerivedTable)
                .HasIndex(entity => entity.DerivedCode)
                .HasDatabaseName(InheritedIndexContract.DerivedIndex)
                .HasPrefixLength(24);
        }
    }

    private sealed class InheritedTpcIndexContext : DbContext
    {
        /// <summary>
        /// Initializes the inherited tpc index context fixture.
        /// </summary>
        public InheritedTpcIndexContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureInheritedTpcIndex(
            modelBuilder,
            InheritedTpcIndexContract.FirstTable,
            InheritedTpcIndexContract.SecondTable,
            InheritedTpcIndexContract.Index,
            prefixLength: 16);
    }

    private sealed class RenamedInheritedTpcIndexContext : DbContext
    {
        /// <summary>
        /// Initializes the renamed inherited tpc index context fixture.
        /// </summary>
        public RenamedInheritedTpcIndexContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureInheritedTpcIndex(
            modelBuilder,
            InheritedTpcIndexContract.RenamedFirstTable,
            InheritedTpcIndexContract.RenamedSecondTable,
            InheritedTpcIndexContract.RenamedIndex,
            prefixLength: 24);
    }

    private static void ConfigureInheritedTpcIndex(
        ModelBuilder modelBuilder,
        string firstTable,
        string secondTable,
        string indexName,
        int prefixLength
    )
    {
        modelBuilder
            .Entity<InheritedTpcIndexBase>()
            .UseTpcMappingStrategy()
            .HasIndex(entity => entity.AlternateId)
            .HasDatabaseName(indexName)
            .HasPrefixLength(prefixLength);

        modelBuilder
            .Entity<FirstInheritedTpcIndex>()
            .ToTable(firstTable);
        modelBuilder
            .Entity<SecondInheritedTpcIndex>()
            .ToTable(secondTable);
    }

    private class InheritedIndexBase
    {
        /// <summary>
        /// Gets or sets the entity key.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the alternate id.
        /// </summary>
        public string AlternateId { get; set; } = string.Empty;
    }

    private sealed class InheritedIndexDerived : InheritedIndexBase
    {
        /// <summary>
        /// Gets or sets the derived code.
        /// </summary>
        public string DerivedCode { get; set; } = string.Empty;
    }

    private abstract class InheritedTpcIndexBase
    {
        /// <summary>
        /// Gets or sets the entity key.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the alternate id.
        /// </summary>
        public string AlternateId { get; set; } = string.Empty;
    }

    private sealed class FirstInheritedTpcIndex : InheritedTpcIndexBase;

    private sealed class SecondInheritedTpcIndex : InheritedTpcIndexBase;

    private static class InheritedIndexContract
    {
        /// <summary>
        /// The base table used by the migration fixture.
        /// </summary>
        public const string BaseTable = "InheritedIndexBase";
        /// <summary>
        /// The derived table used by the migration fixture.
        /// </summary>
        public const string DerivedTable = "InheritedIndexDerived";
        /// <summary>
        /// The base index used by the migration fixture.
        /// </summary>
        public const string BaseIndex = "IX_InheritedIndexBase_AlternateId_Id";
        /// <summary>
        /// The derived index used by the migration fixture.
        /// </summary>
        public const string DerivedIndex = "IX_InheritedIndexDerived_DerivedCode";
    }

    private static class InheritedTpcIndexContract
    {
        /// <summary>
        /// The first table used by the migration fixture.
        /// </summary>
        public const string FirstTable = "FirstInheritedTpcIndex";
        /// <summary>
        /// The second table used by the migration fixture.
        /// </summary>
        public const string SecondTable = "SecondInheritedTpcIndex";
        /// <summary>
        /// The renamed first table used by the migration fixture.
        /// </summary>
        public const string RenamedFirstTable = "RenamedFirstInheritedTpcIndex";
        /// <summary>
        /// The renamed second table used by the migration fixture.
        /// </summary>
        public const string RenamedSecondTable = "RenamedSecondInheritedTpcIndex";
        /// <summary>
        /// The index used by the migration fixture.
        /// </summary>
        public const string Index = "IX_InheritedTpcIndex_AlternateId";
        /// <summary>
        /// The renamed index used by the migration fixture.
        /// </summary>
        public const string RenamedIndex = "IX_InheritedTpcIndex_AlternateId_Renamed";
    }
}
