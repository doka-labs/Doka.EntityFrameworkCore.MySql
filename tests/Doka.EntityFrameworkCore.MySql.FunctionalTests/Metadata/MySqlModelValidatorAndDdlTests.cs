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
