namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Tests defensive input validation: CharSet/StorageEngine identifier allowlist,
/// JSON path property name escaping.
/// </summary>
public sealed class MySqlDefensiveValidationTests
{
    // ── CharSet identifier validation ──

    [Fact]
    public void AppendTableOptions_rejects_charset_with_whitespace_injection()
    {
        using var context = CreateContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new CreateTableOperation
        {
            Name = "InjectionTest",
            Columns =
            {
                new AddColumnOperation
                {
                    Name = "Id",
                    ClrType = typeof(int),
                    ColumnType = "int",
                },
            },
        };
        operation.SetAnnotation(MySqlAnnotationNames.CharSet, "utf8mb4; DROP TABLE users");

        var exception = Assert.Throws<InvalidOperationException>(() => generator.Generate([operation], context.Model));

        Assert.Contains(MySqlAnnotationNames.CharSet, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AppendTableOptions_rejects_storage_engine_with_backtick()
    {
        using var context = CreateContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new CreateTableOperation
        {
            Name = "InjectionTest",
            Columns =
            {
                new AddColumnOperation
                {
                    Name = "Id",
                    ClrType = typeof(int),
                    ColumnType = "int",
                },
            },
        };
        operation.SetAnnotation(MySqlAnnotationNames.StorageEngine, "InnoDB`");

        var exception = Assert.Throws<InvalidOperationException>(() => generator.Generate([operation], context.Model));

        Assert.Contains(MySqlAnnotationNames.StorageEngine, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AppendTableOptions_accepts_valid_charset()
    {
        using var context = CreateContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new CreateTableOperation
        {
            Name = "ValidTest",
            Columns =
            {
                new AddColumnOperation
                {
                    Name = "Id",
                    ClrType = typeof(int),
                    ColumnType = "int",
                },
            },
        };
        operation.SetAnnotation(MySqlAnnotationNames.CharSet, "utf8mb4");

        // Should not throw.
        var commands = generator.Generate([operation], context.Model);
        var sql = string.Join("\n", commands.Select(c => c.CommandText));

        Assert.Contains("CHARACTER SET utf8mb4", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void AlterDatabase_rejects_charset_with_injection()
    {
        using var context = CreateContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AlterDatabaseOperation();
        operation.SetAnnotation(MySqlAnnotationNames.CharSet, "utf8mb4; DROP DATABASE test");

        var exception = Assert.Throws<InvalidOperationException>(() => generator.Generate([operation], context.Model));

        Assert.Contains(MySqlAnnotationNames.CharSet, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AlterDatabase_accepts_valid_charset()
    {
        using var context = CreateContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AlterDatabaseOperation();
        operation.SetAnnotation(MySqlAnnotationNames.CharSet, "utf8mb4");

        var commands = generator.Generate([operation], context.Model);
        var sql = string.Join("\n", commands.Select(c => c.CommandText));

        Assert.Contains("ALTER DATABASE CHARACTER SET = utf8mb4", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void AppendTableOptions_accepts_valid_storage_engine()
    {
        using var context = CreateContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new CreateTableOperation
        {
            Name = "ValidTest",
            Columns =
            {
                new AddColumnOperation
                {
                    Name = "Id",
                    ClrType = typeof(int),
                    ColumnType = "int",
                },
            },
        };
        operation.SetAnnotation(MySqlAnnotationNames.StorageEngine, "InnoDB");

        var commands = generator.Generate([operation], context.Model);
        var sql = string.Join("\n", commands.Select(c => c.CommandText));

        Assert.Contains("ENGINE = InnoDB", sql, StringComparison.Ordinal);
    }

    // ── JSON path property name escaping ──

    [Theory]
    [InlineData("normal", "normal")]
    [InlineData("it's", "it''s")]
    [InlineData("path\\segment", "path\\\\segment")]
    [InlineData("back\\quote'mix", "back\\\\quote''mix")]
    [InlineData("", "")]
    [InlineData("pure'text", "pure''text")]
    public void JsonScalar_path_escapes_special_characters_in_property_name(
        string input,
        string expected
    )
    {
        var method = typeof(MySqlQuerySqlGenerator).GetMethod(
            "EscapeJsonPathPropertyName",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);

        var escaped = (string)method.Invoke(null, [input])!;
        Assert.Equal(expected, escaped);
    }

    // ── Helpers ──

    private static DefensiveContext CreateContext()
    {
        var builder = new DbContextOptionsBuilder<DefensiveContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));
        return new DefensiveContext(builder.Options);
    }

    private sealed class DefensiveEntity
    {
        public int Id { get; set; }
    }

    private sealed class DefensiveContext : DbContext
    {
        public DefensiveContext(
            DbContextOptions<DefensiveContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<DefensiveEntity>(e => e.HasKey(x => x.Id));
    }
}
