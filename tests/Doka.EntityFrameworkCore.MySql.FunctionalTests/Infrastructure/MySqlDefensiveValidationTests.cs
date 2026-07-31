namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Tests defensive input validation: CharSet/StorageEngine identifier allowlist,
/// JSON path property name escaping.
/// </summary>
public sealed class MySqlDefensiveValidationTests
{
    // -- CharSet identifier validation --

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

    // -- JSON path property name escaping --
    //
    // The provider routes JSON path segments through MySqlQuerySqlGenerator's
    // EscapeJsonPathPropertyName instance method. Simple ASCII-identifier names
    // ([_A-Za-z][_A-Za-z0-9]*) pass through unquoted so the generator can emit
    // `$.Name` directly. Anything else flows through BuildQuotedJsonPathSegment
    // which wraps the segment in JSON-path double quotes (`"..."`) and applies
    // JSON-level escapes for `"` and `\`. The complete path subsequently flows
    // through MySqlSqlLiteralGenerator, so the SQL parser never gets a chance to
    // reinterpret those backslashes under a different sql_mode.

    [Theory]
    [InlineData("normal")]
    [InlineData("_underscore_first")]
    [InlineData("Camel123")]
    [InlineData("ALL_CAPS_42")]
    public void JsonScalar_path_passes_simple_identifiers_through_unquoted(
        string input
    )
    {
        Assert.Equal(input, InvokeEscape(input));
    }

    [Theory]
    [InlineData("with space", "\"with space\"")]
    [InlineData("dash-name", "\"dash-name\"")]
    [InlineData("1leading_digit", "\"1leading_digit\"")]
    [InlineData("apo'stroph", "\"apo'stroph\"")]
    [InlineData("", "\"\"")]
    public void JsonScalar_path_wraps_non_identifier_names_in_json_quotes(
        string input,
        string expected
    )
    {
        Assert.Equal(expected, InvokeEscape(input));
    }

    [Theory]
    [InlineData("has\"quote", "\"has\\\"quote\"")]
    [InlineData("has\\back", "\"has\\\\back\"")]
    [InlineData("\"\\", "\"\\\"\\\\\"")]
    public void JsonScalar_path_applies_json_level_escaping_before_sql_literal_generation(
        string input,
        string expected
    )
    {
        Assert.Equal(expected, InvokeEscape(input));
    }

    private static string InvokeEscape(
        string propertyName
    )
    {
        using var context = CreateContext();
        var generator = context.GetService<IQuerySqlGeneratorFactory>().Create();
        var method = generator
            .GetType()
            .GetMethod(
                "EscapeJsonPathPropertyName",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        return (string)method.Invoke(null, [propertyName])!;
    }

    // -- Helpers --

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
