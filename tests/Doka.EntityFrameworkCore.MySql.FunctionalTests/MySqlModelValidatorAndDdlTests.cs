namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Tests for model validation (constraint name lengths, schema rejection),
/// sequence DDL generation (Drop, Alter, Rename), and ServerCapabilities boundaries.
/// </summary>
public sealed class MySqlModelValidatorAndDdlTests
{
    // ── Sequence DDL: Drop ──

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

    // ── Sequence DDL: Create ──

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

    // ── Sequence DDL: Rename ──

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

    // ── ServerCapabilities Boundaries ──

    /// <summary>MySQL 5.6 does not support native JSON (introduced in 5.7).</summary>
    [Fact]
    public void MySQL_5_6_does_not_support_native_json()
    {
        var caps = ServerCapabilities.Create(isMariaDb: false, new Version(5, 6, 0));
        Assert.False(caps.SupportsNativeJsonType);
    }

    /// <summary>MySQL 5.7 supports native JSON.</summary>
    [Fact]
    public void MySQL_5_7_supports_native_json()
    {
        var caps = ServerCapabilities.Create(isMariaDb: false, new Version(5, 7, 0));
        Assert.True(caps.SupportsNativeJsonType);
    }

    /// <summary>MySQL 8.0 supports native JSON.</summary>
    [Fact]
    public void MySQL_8_0_supports_native_json()
    {
        var caps = ServerCapabilities.Create(isMariaDb: false, new Version(8, 0, 0));
        Assert.True(caps.SupportsNativeJsonType);
    }

    /// <summary>MariaDB 10.2 does not support native sequences.</summary>
    [Fact]
    public void MariaDB_10_2_does_not_support_native_sequences()
    {
        var caps = ServerCapabilities.Create(isMariaDb: true, new Version(10, 2, 0));
        Assert.False(caps.SupportsNativeSequences);
    }

    /// <summary>MariaDB 10.3 supports native sequences.</summary>
    [Fact]
    public void MariaDB_10_3_supports_native_sequences()
    {
        var caps = ServerCapabilities.Create(isMariaDb: true, new Version(10, 3, 0));
        Assert.True(caps.SupportsNativeSequences);
    }

    /// <summary>MariaDB 10.4 does not support RETURNING clause.</summary>
    [Fact]
    public void MariaDB_10_4_does_not_support_returning_clause()
    {
        var caps = ServerCapabilities.Create(isMariaDb: true, new Version(10, 4, 0));
        Assert.False(caps.SupportsReturningClause);
    }

    /// <summary>MariaDB 10.5 supports RETURNING clause.</summary>
    [Fact]
    public void MariaDB_10_5_supports_returning_clause()
    {
        var caps = ServerCapabilities.Create(isMariaDb: true, new Version(10, 5, 0));
        Assert.True(caps.SupportsReturningClause);
    }

    /// <summary>MySQL never supports native sequences.</summary>
    [Fact]
    public void MySQL_never_supports_native_sequences()
    {
        var caps = ServerCapabilities.Create(isMariaDb: false, new Version(8, 4, 0));
        Assert.False(caps.SupportsNativeSequences);
    }

    /// <summary>MariaDB always supports full-text indexes.</summary>
    [Fact]
    public void MariaDB_supports_full_text_index()
    {
        var caps = ServerCapabilities.Create(isMariaDb: true, new Version(11, 8, 0));
        Assert.True(caps.SupportsFullTextIndex);
    }

    /// <summary>MySQL 8.0.31+ supports INTERSECT/EXCEPT.</summary>
    [Fact]
    public void MySQL_8_0_31_supports_intersect_except()
    {
        var caps = ServerCapabilities.Create(isMariaDb: false, new Version(8, 0, 31));
        Assert.True(caps.SupportsIntersectExcept);
    }

    /// <summary>MySQL before 8.0.31 does not support INTERSECT/EXCEPT.</summary>
    [Fact]
    public void MySQL_8_0_30_does_not_support_intersect_except()
    {
        var caps = ServerCapabilities.Create(isMariaDb: false, new Version(8, 0, 30));
        Assert.False(caps.SupportsIntersectExcept);
    }

    /// <summary>MariaDB never has native JSON (uses longtext alias).</summary>
    [Fact]
    public void MariaDB_never_supports_native_json()
    {
        var caps = ServerCapabilities.Create(isMariaDb: true, new Version(11, 8, 0));
        Assert.False(caps.SupportsNativeJsonType);
        Assert.True(caps.UsesJsonAliasForJsonColumns);
    }

    // ── Helpers ──

    private static DdlTestContext CreateMySqlContext()
    {
        var builder = new DbContextOptionsBuilder<DdlTestContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));
        return new DdlTestContext(builder.Options);
    }

    private static DdlTestContext CreateMariaDbContext()
    {
        var builder = new DbContextOptionsBuilder<DdlTestContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MariaDb(new Version(11, 8, 0)));
        return new DdlTestContext(builder.Options);
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

    private sealed class DdlTestEntity
    {
        public int Id { get; set; }
    }
}
