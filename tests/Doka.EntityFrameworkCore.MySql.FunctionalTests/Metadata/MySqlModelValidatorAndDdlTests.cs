namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Tests for model validation (constraint name lengths, schema qualification),
/// sequence DDL generation (Drop, Alter, Rename), and ServerCapabilities boundaries.
/// </summary>
public sealed class MySqlModelValidatorAndDdlTests
{
    /// <summary>
    /// Verifies that native full-text indexes can target the unbounded text types
    /// supported by MySQL and MariaDB.
    /// </summary>
    [Fact]
    public void Full_text_index_accepts_unbounded_text_property()
    {
        using var context = new FullTextIndexContext(CreateOptions<FullTextIndexContext>());

        Assert.NotNull(context.Model.FindEntityType(typeof(FullTextIndexEntity)));
    }

    /// <summary>
    /// Verifies that ordinary indexes still reject an unbounded text key part.
    /// </summary>
    [Fact]
    public void Ordinary_index_rejects_unbounded_text_property()
    {
        using var context = new OrdinaryTextIndexContext(CreateOptions<OrdinaryTextIndexContext>());

        Assert.Throws<InvalidOperationException>(() => _ = context.Model);
    }

    /// <summary>
    /// An explicitly bounded utf8mb4 index that exceeds the largest InnoDB key
    /// budget is rejected before SQL generation.
    /// </summary>
    [Fact]
    public void Utf8Mb4_full_index_rejects_one_byte_over_absolute_limit()
    {
        using var context = new IndexWidthContext<Utf8Mb4OverlongScenario>(
            CreateOptions<IndexWidthContext<Utf8Mb4OverlongScenario>>());

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains("3200 bytes", exception.Message, StringComparison.Ordinal);
        Assert.Contains("3072-byte", exception.Message, StringComparison.Ordinal);
        Assert.Contains("does not invent a prefix", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The full-column utf8mb4 boundary remains valid and is not rewritten to a
    /// prefix index.
    /// </summary>
    [Fact]
    public void Utf8Mb4_full_index_accepts_absolute_limit()
    {
        using var context = new IndexWidthContext<Utf8Mb4BoundaryScenario>(
            CreateOptions<IndexWidthContext<Utf8Mb4BoundaryScenario>>());

        var index = Assert.Single(context.Model.FindEntityType(typeof(IndexWidthEntity))!.GetIndexes());

        Assert.Null(index.GetMySqlIndexPrefixLengths());
    }

    /// <summary>
    /// A deliberate prefix is measured instead of the complete property.
    /// </summary>
    [Fact]
    public void Utf8Mb4_explicit_prefix_accepts_absolute_limit()
    {
        using var context = new IndexWidthContext<Utf8Mb4PrefixBoundaryScenario>(
            CreateOptions<IndexWidthContext<Utf8Mb4PrefixBoundaryScenario>>());

        var index = Assert.Single(context.Model.FindEntityType(typeof(IndexWidthEntity))!.GetIndexes());

        Assert.Equal([768], index.GetMySqlIndexPrefixLengths());
    }

    /// <summary>
    /// Prefixes are subject to the same byte budget as complete key parts.
    /// </summary>
    [Fact]
    public void Utf8Mb4_explicit_prefix_rejects_one_character_over_absolute_limit()
    {
        using var context = new IndexWidthContext<Utf8Mb4PrefixOverlongScenario>(
            CreateOptions<IndexWidthContext<Utf8Mb4PrefixOverlongScenario>>());

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains("3076 bytes", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A prefix cannot claim more characters than the bounded column stores.
    /// </summary>
    [Fact]
    public void Prefix_longer_than_column_is_rejected()
    {
        using var context = new IndexWidthContext<PrefixBeyondColumnScenario>(
            CreateOptions<IndexWidthContext<PrefixBeyondColumnScenario>>());

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains("exceeds its store length 32", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Composite definitions include fixed-width key parts in their aggregate
    /// byte budget.
    /// </summary>
    [Fact]
    public void Composite_index_rejects_aggregate_width_over_absolute_limit()
    {
        using var context = new IndexWidthContext<CompositeOverlongScenario>(
            CreateOptions<IndexWidthContext<CompositeOverlongScenario>>());

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains("3076 bytes", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Composite definitions exactly at the byte boundary remain valid.
    /// </summary>
    [Fact]
    public void Composite_index_accepts_aggregate_width_at_absolute_limit()
    {
        using var context = new IndexWidthContext<CompositeBoundaryScenario>(
            CreateOptions<IndexWidthContext<CompositeBoundaryScenario>>());

        Assert.Single(context.Model.FindEntityType(typeof(IndexWidthEntity))!.GetIndexes());
    }

    /// <summary>
    /// Character-set width is part of validation; a single-byte charset does
    /// not inherit utf8mb4's four-byte multiplier.
    /// </summary>
    [Fact]
    public void Latin1_full_index_accepts_length_that_utf8mb4_rejects()
    {
        using var context = new IndexWidthContext<Latin1Scenario>(
            CreateOptions<IndexWidthContext<Latin1Scenario>>());

        Assert.Single(context.Model.FindEntityType(typeof(IndexWidthEntity))!.GetIndexes());
    }

    /// <summary>
    /// Three-byte character sets retain their own full-key boundary instead of
    /// inheriting either the latin1 or utf8mb4 calculation.
    /// </summary>
    [Fact]
    public void Utf8Mb3_full_index_accepts_absolute_limit()
    {
        using var context = new IndexWidthContext<Utf8Mb3BoundaryScenario>(
            CreateOptions<IndexWidthContext<Utf8Mb3BoundaryScenario>>());

        Assert.Single(context.Model.FindEntityType(typeof(IndexWidthEntity))!.GetIndexes());
    }

    /// <summary>
    /// A three-byte character-set definition one character beyond the absolute
    /// key budget is rejected.
    /// </summary>
    [Fact]
    public void Utf8Mb3_full_index_rejects_one_character_over_absolute_limit()
    {
        using var context = new IndexWidthContext<Utf8Mb3OverlongScenario>(
            CreateOptions<IndexWidthContext<Utf8Mb3OverlongScenario>>());

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains("3075 bytes", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An explicit property collation determines the encoded width before a
    /// broader model character-set default.
    /// </summary>
    [Fact]
    public void Property_collation_overrides_model_character_set_for_index_width()
    {
        using var context = new IndexWidthContext<CollationOverrideScenario>(
            CreateOptions<IndexWidthContext<CollationOverrideScenario>>());

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains("3200 bytes", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Binary key parts are measured in bytes without a character-set multiplier.
    /// </summary>
    [Fact]
    public void Binary_full_index_rejects_one_byte_over_absolute_limit()
    {
        using var context = new IndexWidthContext<BinaryOverlongScenario>(
            CreateOptions<IndexWidthContext<BinaryOverlongScenario>>());

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains("3073 bytes", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Binary key lengths are byte-exact at the absolute boundary.
    /// </summary>
    [Fact]
    public void Binary_full_index_accepts_absolute_limit()
    {
        using var context = new IndexWidthContext<BinaryBoundaryScenario>(
            CreateOptions<IndexWidthContext<BinaryBoundaryScenario>>());

        Assert.Single(context.Model.FindEntityType(typeof(IndexWidthEntity))!.GetIndexes());
    }

    /// <summary>
    /// Unique and key definitions share the same fail-closed byte contract.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Unique_and_alternate_key_definitions_reject_overlong_utf8mb4_values(
        bool uniqueIndex
    )
    {
        var exception = uniqueIndex
            ? Assert.Throws<InvalidOperationException>(() =>
            {
                using var context = new IndexWidthContext<UniqueOverlongScenario>(
                    CreateOptions<IndexWidthContext<UniqueOverlongScenario>>());
                _ = context.Model;
            })
            : Assert.Throws<InvalidOperationException>(() =>
            {
                using var context = new IndexWidthContext<AlternateKeyOverlongScenario>(
                    CreateOptions<IndexWidthContext<AlternateKeyOverlongScenario>>());
                _ = context.Model;
            });

        Assert.Contains("3200 bytes", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Primary keys are evaluated by the same complete-definition contract as
    /// alternate keys and indexes.
    /// </summary>
    [Fact]
    public void Primary_key_rejects_overlong_utf8mb4_value()
    {
        using var context = new IndexWidthContext<PrimaryKeyOverlongScenario>(
            CreateOptions<IndexWidthContext<PrimaryKeyOverlongScenario>>());

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains("key 'PK_IndexWidthEntity'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("3200 bytes", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The convention-created supporting index for a foreign key is validated
    /// without requiring an explicit HasIndex call.
    /// </summary>
    [Fact]
    public void Foreign_key_supporting_index_accepts_absolute_limit()
    {
        using var context = new ForeignKeyIndexWidthContext(
            CreateOptions<ForeignKeyIndexWidthContext>());

        var dependent = context.Model.FindEntityType(typeof(ForeignKeyIndexWidthDependent));
        var index = Assert.Single(dependent!.GetIndexes());

        Assert.True(index.Properties.Single().IsForeignKey());
    }

    [Fact]
    public void Default_table_and_view_schemas_are_preserved_as_database_qualifiers()
    {
        using var context = CreateSchemaContext();

        Assert.Equal("default_database", context.Model.GetDefaultSchema());

        var tableEntity = context.Model.FindEntityType(typeof(DdlTestEntity));

        Assert.NotNull(tableEntity);
        Assert.Equal("table_database", tableEntity.GetSchema());

        var viewEntity = context.Model.FindEntityType(typeof(DdlTestView));

        Assert.NotNull(viewEntity);
        Assert.Equal("view_database", viewEntity.GetViewSchema());
    }

    // -- Sequence DDL: Drop --

    [Fact]
    public void DropSequence_generates_mysql_drop_table_for_emulated_sequence()
    {
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new DropSequenceOperation { Name = "TestSequence" };

        var commands = generator.Generate([operation], context.Model);
        var sql = string.Join("\n", commands.Select(c => c.CommandText));

        Assert.Contains("DROP TABLE IF EXISTS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("__efsequence_TestSequence", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void DropSequence_generates_mariadb_native_drop_sequence()
    {
        using var context = CreateMariaDbContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new DropSequenceOperation { Name = "TestSequence" };

        var commands = generator.Generate([operation], context.Model);
        var sql = string.Join("\n", commands.Select(c => c.CommandText));

        Assert.Contains("DROP SEQUENCE IF EXISTS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`TestSequence`", sql, StringComparison.Ordinal);
    }

    // -- Sequence DDL: Create --

    [Fact]
    public void CreateSequence_generates_mysql_table_emulation()
    {
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new CreateSequenceOperation
        {
            Name = "OrderSequence",
            StartValue = 100,
            IncrementBy = 5,
            ClrType = typeof(long),
        };

        var commands = generator.Generate([operation], context.Model);
        var sql = string.Join("\n", commands.Select(c => c.CommandText));

        Assert.Contains("__efsequence_OrderSequence", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateSequence_generates_mariadb_native_create_sequence()
    {
        using var context = CreateMariaDbContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new CreateSequenceOperation
        {
            Name = "OrderSequence",
            StartValue = 100,
            IncrementBy = 5,
            ClrType = typeof(long),
        };

        var commands = generator.Generate([operation], context.Model);
        var sql = string.Join("\n", commands.Select(c => c.CommandText));

        Assert.Contains("CREATE SEQUENCE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`OrderSequence`", sql, StringComparison.Ordinal);
        Assert.Contains("START WITH 100", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INCREMENT BY 5", sql, StringComparison.OrdinalIgnoreCase);
    }

    // -- Sequence DDL: Rename --

    [Fact]
    public void RenameSequence_generates_mysql_rename_table()
    {
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new RenameSequenceOperation
        {
            Name = "OldSeq",
            NewName = "NewSeq"
        };

        var commands = generator.Generate([operation], context.Model);
        var sql = string.Join("\n", commands.Select(c => c.CommandText));

        Assert.Contains("RENAME TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("__efsequence_OldSeq", sql, StringComparison.Ordinal);
        Assert.Contains("__efsequence_NewSeq", sql, StringComparison.Ordinal);
    }

    // -- ServerCapabilities Boundaries --

    /// <summary>MySQL 5.7.7 does not support native JSON.</summary>
    [Fact]
    public void MySQL_5_7_7_does_not_support_native_json()
    {
        var caps = EngineProfileTable.Resolve(EngineFamily.MySql, new Version(5, 7, 7));

        Assert.False(caps.Has(EngineCapability.NativeJsonType));
    }

    /// <summary>MySQL 5.7.8 supports native JSON.</summary>
    [Fact]
    public void MySQL_5_7_8_supports_native_json()
    {
        var caps = EngineProfileTable.Resolve(EngineFamily.MySql, new Version(5, 7, 8));

        Assert.True(caps.Has(EngineCapability.NativeJsonType));
    }

    /// <summary>MySQL 8.0 supports native JSON.</summary>
    [Fact]
    public void MySQL_8_0_supports_native_json()
    {
        var caps = EngineProfileTable.Resolve(EngineFamily.MySql, new Version(8, 0, 0));

        Assert.True(caps.Has(EngineCapability.NativeJsonType));
    }

    /// <summary>MariaDB 10.2 does not support native sequences.</summary>
    [Fact]
    public void MariaDB_10_2_does_not_support_native_sequences()
    {
        var caps = EngineProfileTable.Resolve(EngineFamily.MariaDb, new Version(10, 2, 0));

        Assert.False(caps.Has(EngineCapability.NativeSequences));
    }

    /// <summary>MariaDB 10.3 supports native sequences.</summary>
    [Fact]
    public void MariaDB_10_3_supports_native_sequences()
    {
        var caps = EngineProfileTable.Resolve(EngineFamily.MariaDb, new Version(10, 3, 0));

        Assert.True(caps.Has(EngineCapability.NativeSequences));
    }

    /// <summary>MariaDB 10.4 does not support RETURNING clause.</summary>
    [Fact]
    public void MariaDB_10_4_does_not_support_returning_clause()
    {
        var caps = EngineProfileTable.Resolve(EngineFamily.MariaDb, new Version(10, 4, 0));

        Assert.False(caps.Has(EngineCapability.ReturningClause));
    }

    /// <summary>MariaDB 10.5 supports RETURNING clause.</summary>
    [Fact]
    public void MariaDB_10_5_supports_returning_clause()
    {
        var caps = EngineProfileTable.Resolve(EngineFamily.MariaDb, new Version(10, 5, 0));

        Assert.True(caps.Has(EngineCapability.ReturningClause));
    }

    /// <summary>MySQL never supports native sequences.</summary>
    [Fact]
    public void MySQL_never_supports_native_sequences()
    {
        var caps = EngineProfileTable.Resolve(EngineFamily.MySql, new Version(8, 4, 0));

        Assert.False(caps.Has(EngineCapability.NativeSequences));
    }

    /// <summary>MariaDB never has native JSON (uses longtext alias).</summary>
    [Fact]
    public void MariaDB_never_supports_native_json()
    {
        var profile = MySqlServerVersion.MariaDb(new Version(11, 8, 0)).Profile;

        Assert.False(profile.Engine.Has(EngineCapability.NativeJsonType));
        Assert.Equal(ProviderSupportStatus.Emulated, profile.GetSupport(ProviderCapability.JsonColumns));
    }

    // -- Helpers --

    private static DdlTestContext CreateMySqlContext()
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<DdlTestContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));
        return new DdlTestContext(builder.Options);
    }

    private static DdlTestContext CreateMariaDbContext()
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<DdlTestContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MariaDb(new Version(11, 8, 0)));
        return new DdlTestContext(builder.Options);
    }

    private static SchemaDdlTestContext CreateSchemaContext()
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<SchemaDdlTestContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));
        return new SchemaDdlTestContext(builder.Options);
    }

    private static DbContextOptions<TContext> CreateOptions<TContext>()
        where TContext : DbContext
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<TContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        return builder.Options;
    }

    private sealed class DdlTestContext : DbContext
    {
        public DdlTestContext(
            DbContextOptions<DdlTestContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<DdlTestEntity>(e => { e.HasKey(x => x.Id); });
    }

    private sealed class SchemaDdlTestContext : DbContext
    {
        public SchemaDdlTestContext(
            DbContextOptions<SchemaDdlTestContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.HasDefaultSchema("default_database");

            modelBuilder.Entity<DdlTestEntity>(entity =>
            {
                entity.HasKey(item => item.Id);
                entity.ToTable("DdlTestEntities", "table_database");
            });

            modelBuilder.Entity<DdlTestView>(entity =>
            {
                entity.HasNoKey();
                entity.ToView("DdlTestView", "view_database");
            });
        }
    }

    private sealed class FullTextIndexContext : DbContext
    {
        public FullTextIndexContext(
            DbContextOptions<FullTextIndexContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<FullTextIndexEntity>(entity =>
            {
                entity
                    .Property(item => item.Body)
                    .HasColumnType("text");
                entity
                    .HasIndex(item => item.Body)
                    .IsFullText();
            });
        }
    }

    private sealed class OrdinaryTextIndexContext : DbContext
    {
        public OrdinaryTextIndexContext(
            DbContextOptions<OrdinaryTextIndexContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<FullTextIndexEntity>(entity =>
            {
                entity
                    .Property(item => item.Body)
                    .HasColumnType("text");
                entity.HasIndex(item => item.Body);
            });
        }
    }

    private sealed class IndexWidthContext<TScenario> : DbContext
        where TScenario : class
    {
        public IndexWidthContext(
            DbContextOptions<IndexWidthContext<TScenario>> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var scenarioType = typeof(TScenario);
            var charSet = scenarioType switch
            {
                var current when current == typeof(Latin1Scenario)
                    || current == typeof(CollationOverrideScenario) => "latin1",
                var current when current == typeof(Utf8Mb3BoundaryScenario)
                    || current == typeof(Utf8Mb3OverlongScenario) => "utf8mb3",
                _ => "utf8mb4",
            };
            modelBuilder.HasCharSet(charSet);

            modelBuilder.Entity<IndexWidthEntity>(entity =>
            {
                entity.HasKey(item => item.Id);

                if (scenarioType == typeof(BinaryOverlongScenario)
                    || scenarioType == typeof(BinaryBoundaryScenario))
                {
                    entity
                        .Property(item => item.BinaryValue)
                        .HasColumnType(
                            scenarioType == typeof(BinaryBoundaryScenario)
                                ? "varbinary(3072)"
                                : "varbinary(3073)");
                    entity.HasIndex(item => item.BinaryValue);
                    return;
                }

                var length = scenarioType switch
                {
                    var current when current == typeof(Utf8Mb4BoundaryScenario) => 768,
                    var current when current == typeof(PrefixBeyondColumnScenario) => 32,
                    var current when current == typeof(CompositeBoundaryScenario) => 767,
                    var current when current == typeof(CompositeOverlongScenario) => 768,
                    var current when current == typeof(Utf8Mb3BoundaryScenario) => 1024,
                    var current when current == typeof(Utf8Mb3OverlongScenario) => 1025,
                    _ => 800,
                };

                var property = entity
                    .Property(item => item.Value)
                    .HasColumnType($"varchar({length})");

                if (scenarioType == typeof(CollationOverrideScenario))
                {
                    property.UseCollation("utf8mb4_bin");
                }

                if (scenarioType == typeof(PrimaryKeyOverlongScenario))
                {
                    entity.HasKey(item => item.Value);
                    return;
                }

                if (scenarioType == typeof(AlternateKeyOverlongScenario))
                {
                    entity.HasAlternateKey(item => item.Value);
                    return;
                }

                var index = scenarioType == typeof(CompositeBoundaryScenario)
                    || scenarioType == typeof(CompositeOverlongScenario)
                        ? entity.HasIndex(item => new { item.Value, item.Id })
                        : entity.HasIndex(item => item.Value);

                if (scenarioType == typeof(UniqueOverlongScenario))
                {
                    index.IsUnique();
                }
                else if (scenarioType == typeof(Utf8Mb4PrefixBoundaryScenario))
                {
                    index.HasPrefixLength(768);
                }
                else if (scenarioType == typeof(Utf8Mb4PrefixOverlongScenario))
                {
                    index.HasPrefixLength(769);
                }
                else if (scenarioType == typeof(PrefixBeyondColumnScenario))
                {
                    index.HasPrefixLength(33);
                }
            });
        }
    }

    private sealed class Utf8Mb4OverlongScenario;

    private sealed class Utf8Mb4BoundaryScenario;

    private sealed class Utf8Mb4PrefixBoundaryScenario;

    private sealed class Utf8Mb4PrefixOverlongScenario;

    private sealed class PrefixBeyondColumnScenario;

    private sealed class CompositeBoundaryScenario;

    private sealed class CompositeOverlongScenario;

    private sealed class Latin1Scenario;

    private sealed class Utf8Mb3BoundaryScenario;

    private sealed class Utf8Mb3OverlongScenario;

    private sealed class CollationOverrideScenario;

    private sealed class BinaryBoundaryScenario;

    private sealed class BinaryOverlongScenario;

    private sealed class UniqueOverlongScenario;

    private sealed class AlternateKeyOverlongScenario;

    private sealed class PrimaryKeyOverlongScenario;

    private sealed class ForeignKeyIndexWidthContext : DbContext
    {
        public ForeignKeyIndexWidthContext(
            DbContextOptions<ForeignKeyIndexWidthContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.HasCharSet("utf8mb4");

            modelBuilder.Entity<ForeignKeyIndexWidthPrincipal>(entity =>
            {
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.NaturalKey)
                    .HasColumnType("varchar(768)");
                entity.HasAlternateKey(item => item.NaturalKey);
            });

            modelBuilder.Entity<ForeignKeyIndexWidthDependent>(entity =>
            {
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.PrincipalNaturalKey)
                    .HasColumnType("varchar(768)");
                entity
                    .HasOne<ForeignKeyIndexWidthPrincipal>()
                    .WithMany()
                    .HasForeignKey(item => item.PrincipalNaturalKey)
                    .HasPrincipalKey(item => item.NaturalKey);
            });
        }
    }

    private sealed class ForeignKeyIndexWidthPrincipal
    {
        public int Id { get; set; }

        public string NaturalKey { get; set; } = string.Empty;
    }

    private sealed class ForeignKeyIndexWidthDependent
    {
        public int Id { get; set; }

        public string PrincipalNaturalKey { get; set; } = string.Empty;
    }

    private sealed class IndexWidthEntity
    {
        public int Id { get; set; }

        public string Value { get; set; } = string.Empty;

        public byte[] BinaryValue { get; set; } = [];
    }

    private sealed class FullTextIndexEntity
    {
        public int Id { get; set; }

        public string Body { get; set; } = string.Empty;
    }

    private sealed class DdlTestEntity
    {
        public int Id { get; set; }
    }

    private sealed class DdlTestView
    {
        public int Id { get; set; }
    }
}
