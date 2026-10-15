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

        var sql = Assert.Single(generator.Generate([operation], context.Model)).CommandText;

        Assert.Equal("ALTER DATABASE CHARACTER SET = utf8mb4;\n", sql);
    }

    [Fact]
    public void AlterDatabase_accepts_valid_collation_without_charset()
    {
        using var context = CreateContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AlterDatabaseOperation
        {
            Collation = "utf8mb4_unicode_ci",
        };

        var sql = Assert.Single(generator.Generate([operation], context.Model)).CommandText;

        Assert.Equal("ALTER DATABASE COLLATE = utf8mb4_unicode_ci;\n", sql);
    }

    [Fact]
    public void AlterDatabase_accepts_compatible_charset_and_collation()
    {
        using var context = CreateContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AlterDatabaseOperation
        {
            Collation = "utf8mb4_unicode_ci",
        };
        operation.SetAnnotation(MySqlAnnotationNames.CharSet, "utf8mb4");

        var sql = Assert.Single(generator.Generate([operation], context.Model)).CommandText;

        Assert.Equal(
            "ALTER DATABASE CHARACTER SET = utf8mb4 COLLATE = utf8mb4_unicode_ci;\n",
            sql);
    }

    [Fact]
    public void AlterDatabase_accepts_shared_mariadb_uca1400_collation_for_unicode_charset()
    {
        using var context = CreateContext(MySqlServerVersion.MariaDb(new Version(11, 8, 0)));
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AlterDatabaseOperation
        {
            Collation = "uca1400_ai_ci",
        };
        operation.SetAnnotation(MySqlAnnotationNames.CharSet, "utf8mb4");

        var sql = Assert.Single(generator.Generate([operation], context.Model)).CommandText;

        Assert.Equal(
            "ALTER DATABASE CHARACTER SET = utf8mb4 COLLATE = uca1400_ai_ci;\n",
            sql);
    }

    [Fact]
    public void AlterDatabase_rejects_mariadb_only_shared_collation_for_mysql()
    {
        using var context = CreateContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AlterDatabaseOperation
        {
            Collation = "uca1400_ai_ci",
        };
        operation.SetAnnotation(MySqlAnnotationNames.CharSet, "utf8mb4");

        var exception = Assert.Throws<InvalidOperationException>(
            () => generator.Generate([operation], context.Model));

        Assert.Contains(RelationalAnnotationNames.Collation, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void AlterDatabase_rejects_blank_collation(
        string collation
    )
    {
        using var context = CreateContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AlterDatabaseOperation
        {
            Collation = collation,
        };

        Assert.Throws<ArgumentException>(() => generator.Generate([operation], context.Model));
    }

    [Fact]
    public void AlterDatabase_rejects_collation_with_injection()
    {
        using var context = CreateContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AlterDatabaseOperation
        {
            Collation = "utf8mb4_unicode_ci; DROP DATABASE test",
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => generator.Generate([operation], context.Model));

        Assert.Contains(RelationalAnnotationNames.Collation, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(operation.Collation, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AlterDatabase_rejects_incompatible_charset_and_collation()
    {
        using var context = CreateContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AlterDatabaseOperation
        {
            Collation = "latin1_swedish_ci",
        };
        operation.SetAnnotation(MySqlAnnotationNames.CharSet, "utf8mb4");

        var exception = Assert.Throws<InvalidOperationException>(
            () => generator.Generate([operation], context.Model));

        Assert.Contains(MySqlAnnotationNames.CharSet, exception.Message, StringComparison.Ordinal);
        Assert.Contains(RelationalAnnotationNames.Collation, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AlterDatabase_rejects_table_collation_annotation()
    {
        using var context = CreateContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AlterDatabaseOperation
        {
            Collation = "utf8mb4_unicode_ci",
        };
        operation.SetAnnotation(MySqlAnnotationNames.Collation, "utf8mb4_unicode_ci");

        var exception = Assert.Throws<InvalidOperationException>(
            () => generator.Generate([operation], context.Model));

        Assert.Contains(nameof(AlterDatabaseOperation.Collation), exception.Message, StringComparison.Ordinal);
        Assert.Contains(MySqlAnnotationNames.Collation, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AlterDatabase_rejects_non_string_charset_annotation()
    {
        using var context = CreateContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AlterDatabaseOperation();
        operation.SetAnnotation(MySqlAnnotationNames.CharSet, 42);

        var exception = Assert.Throws<InvalidOperationException>(
            () => generator.Generate([operation], context.Model));

        Assert.Contains(MySqlAnnotationNames.CharSet, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AlterDatabase_without_database_options_emits_no_command()
    {
        using var context = CreateContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();

        var commands = generator.Generate([new AlterDatabaseOperation()], context.Model);

        Assert.Empty(commands);
    }

    [Fact]
    public void AlterDatabase_rejects_removing_last_explicit_database_default()
    {
        using var context = CreateContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AlterDatabaseOperation();
        operation.OldDatabase.Collation = "utf8mb4_bin";

        var exception = Assert.Throws<InvalidOperationException>(
            () => generator.Generate([operation], context.Model));

        Assert.Contains("explicit target value", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AlterDatabase_accepts_explicit_charset_replacing_old_collation()
    {
        using var context = CreateContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AlterDatabaseOperation();
        operation.SetAnnotation(MySqlAnnotationNames.CharSet, "utf8mb4");
        operation.OldDatabase.Collation = "utf8mb4_bin";

        var sql = Assert.Single(generator.Generate([operation], context.Model)).CommandText;

        Assert.Equal("ALTER DATABASE CHARACTER SET = utf8mb4;\n", sql);
    }

    [Fact]
    public void AlterTable_rejects_removed_collation_without_resolved_target_default()
    {
        using var context = CreateContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AlterTableOperation
        {
            Name = "ResetCollation",
        };
        operation.OldTable.SetAnnotation(RelationalAnnotationNames.Collation, "utf8mb4_bin");

        var exception = Assert.Throws<InvalidOperationException>(
            () => generator.Generate([operation], context.Model));

        Assert.Contains(RelationalAnnotationNames.Collation, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AlterTable_rejects_removed_charset_without_resolved_target_default()
    {
        using var context = CreateContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AlterTableOperation
        {
            Name = "RemovedCharSet",
        };
        operation.OldTable.SetAnnotation(MySqlAnnotationNames.CharSet, "latin1");

        var exception = Assert.Throws<InvalidOperationException>(
            () => generator.Generate([operation], context.Model));

        Assert.Contains(MySqlAnnotationNames.CharSet, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AlterTable_emits_explicit_storage_engine_transition()
    {
        using var context = CreateContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AlterTableOperation
        {
            Name = "StorageEngineTransition",
        };
        operation.SetAnnotation(MySqlAnnotationNames.StorageEngine, "InnoDB");
        operation.OldTable.SetAnnotation(MySqlAnnotationNames.StorageEngine, "MyISAM");

        var sql = Assert.Single(generator.Generate([operation], context.Model)).CommandText;

        Assert.Equal("ALTER TABLE `StorageEngineTransition` ENGINE = InnoDB;\n", sql);
    }

    [Fact]
    public void AlterTable_emits_known_collation_together_with_changed_charset()
    {
        using var context = CreateContext(MySqlServerVersion.MariaDb(new Version(11, 8, 0)));
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AlterTableOperation
        {
            Name = "SharedCollationTransition",
        };
        operation.SetAnnotation(MySqlAnnotationNames.CharSet, "utf8mb4");
        operation.SetAnnotation(RelationalAnnotationNames.Collation, "uca1400_ai_ci");
        operation.OldTable.SetAnnotation(MySqlAnnotationNames.CharSet, "utf8mb3");
        operation.OldTable.SetAnnotation(RelationalAnnotationNames.Collation, "uca1400_ai_ci");

        var sql = Assert.Single(generator.Generate([operation], context.Model)).CommandText;

        Assert.Equal(
            "ALTER TABLE `SharedCollationTransition` DEFAULT CHARACTER SET = utf8mb4 "
            + "DEFAULT COLLATE = uca1400_ai_ci;\n",
            sql);
    }

    [Fact]
    public void AlterTable_ignores_case_only_table_option_differences()
    {
        using var context = CreateContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AlterTableOperation
        {
            Name = "CaseOnlyTransition",
        };
        operation.SetAnnotation(MySqlAnnotationNames.CharSet, "UTF8MB4");
        operation.SetAnnotation(RelationalAnnotationNames.Collation, "UTF8MB4_UNICODE_CI");
        operation.SetAnnotation(MySqlAnnotationNames.StorageEngine, "INNODB");
        operation.OldTable.SetAnnotation(MySqlAnnotationNames.CharSet, "utf8mb4");
        operation.OldTable.SetAnnotation(RelationalAnnotationNames.Collation, "utf8mb4_unicode_ci");
        operation.OldTable.SetAnnotation(MySqlAnnotationNames.StorageEngine, "InnoDB");

        var commands = generator.Generate([operation], context.Model);

        Assert.Empty(commands);
    }

    [Fact]
    public void AlterTable_rejects_removed_storage_engine_without_explicit_target()
    {
        using var context = CreateContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AlterTableOperation
        {
            Name = "RemovedStorageEngine",
        };
        operation.OldTable.SetAnnotation(MySqlAnnotationNames.StorageEngine, "InnoDB");

        var exception = Assert.Throws<InvalidOperationException>(
            () => generator.Generate([operation], context.Model));

        Assert.Contains(MySqlAnnotationNames.StorageEngine, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AppendTableOptions_accepts_canonical_relational_collation()
    {
        using var context = CreateContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = CreateTableOperation("CanonicalCollation");
        operation.SetAnnotation(RelationalAnnotationNames.Collation, "utf8mb4_bin");

        var sql = Assert.Single(generator.Generate([operation], context.Model)).CommandText;

        Assert.Contains("COLLATE utf8mb4_bin", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void AppendTableOptions_rejects_conflicting_collation_annotations()
    {
        using var context = CreateContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = CreateTableOperation("ConflictingCollation");
        operation.SetAnnotation(RelationalAnnotationNames.Collation, "utf8mb4_unicode_ci");
        operation.SetAnnotation(MySqlAnnotationNames.Collation, "utf8mb4_bin");

        var exception = Assert.Throws<InvalidOperationException>(
            () => generator.Generate([operation], context.Model));

        Assert.Contains(RelationalAnnotationNames.Collation, exception.Message, StringComparison.Ordinal);
        Assert.Contains(MySqlAnnotationNames.Collation, exception.Message, StringComparison.Ordinal);
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

    private static CreateTableOperation CreateTableOperation(
        string name
    ) => new()
    {
        Name = name,
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

        Assert.Contains(RelationalAnnotationNames.Collation, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(collation, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ColumnDefinition_rejects_blank_collation(
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

        _ = Assert.Throws<ArgumentException>(
            () => generator.Generate([operation], context.Model));
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

        var exception = Assert.Throws<InvalidOperationException>(query.ToQueryString);

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

    private static DefensiveContext CreateContext(
        MySqlServerVersion? serverVersion = null
    )
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<DefensiveContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            serverVersion ?? MySqlServerVersion.MySql(new Version(8, 4, 0)));
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
