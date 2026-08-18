namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Tests defensive input validation for SQL grammar tokens and JSON path
/// property-name escaping.
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

    [Theory]
    [InlineData("utf8mb4_bin; SELECT 'injected'")]
    [InlineData("utf8mb4_unicode_ci\u00e9")]
    public void ColumnDefinition_rejects_invalid_collation_tokens(
        string collation
    )
    {
        using var context = CreateContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AddColumnOperation
        {
            Table = "DefensiveEntities",
            Name = "Name",
            ClrType = typeof(string),
            ColumnType = "varchar(100)",
            Collation = collation,
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => generator.Generate([operation], context.Model));

        Assert.Contains(MySqlAnnotationNames.Collation, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(collation, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ColumnDefinition_accepts_valid_collation_token()
    {
        using var context = CreateContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AddColumnOperation
        {
            Table = "DefensiveEntities",
            Name = "Name",
            ClrType = typeof(string),
            ColumnType = "varchar(100)",
            Collation = "utf8mb4_0900_ai_ci",
        };

        var sql = Assert.Single(generator.Generate([operation], context.Model)).CommandText;

        Assert.Contains("COLLATE utf8mb4_0900_ai_ci", sql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("utf8mb4_bin; SELECT 'injected'")]
    [InlineData("utf8mb4_unicode_ci\u00e9")]
    public void Query_generation_rejects_invalid_collation_tokens(
        string collation
    )
    {
        using var context = CreateContext();

        var query = context
            .Set<DefensiveEntity>()
            .Where(entity => EF.Functions.Collate(entity.Name, collation) == "value");

        var exception = Assert.Throws<InvalidOperationException>(() => query.ToQueryString());

        Assert.Contains(MySqlAnnotationNames.Collation, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(collation, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Query_generation_accepts_valid_collation_token()
    {
        using var context = CreateContext();

        var sql = context
            .Set<DefensiveEntity>()
            .Where(entity => EF.Functions.Collate(entity.Name, "utf8mb4_bin") == "value")
            .ToQueryString();

        Assert.Contains("COLLATE utf8mb4_bin", sql, StringComparison.Ordinal);
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
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<DefensiveContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));
        return new DefensiveContext(builder.Options);
    }

    private sealed class DefensiveEntity
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
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
