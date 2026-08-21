namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies provider-owned column-default and special-column SQL contracts.
/// </summary>
public sealed class MySqlColumnDefaultSqlGenerationTests
{
    /// <summary>
    /// Verifies typed temporal literals in every column-definition operation.
    /// </summary>
    [Theory]
    [InlineData("create")]
    [InlineData("add")]
    [InlineData("alter")]
    public void Temporal_literal_defaults_are_parenthesized_in_every_column_path(
        string path
    )
    {
        using var context = CreateContext(MySqlServerVersion.MySql(new Version(8, 4, 11)));
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operations = CreateTemporalDefaultOperations(path);

        var sql = JoinSql(generator.Generate(operations, context.Model));

        Assert.Contains("DEFAULT (DATE '2026-08-17')", sql, StringComparison.Ordinal);
        Assert.Contains("DEFAULT (TIME '12:34:56.123456')", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DEFAULT DATE '", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DEFAULT TIME '", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that inferred and modifier-bearing store types use the same
    /// expression-default policy as canonical store types.
    /// </summary>
    [Fact]
    public void Default_policy_handles_inferred_and_modifier_bearing_store_types()
    {
        using var context = CreateContext(MySqlServerVersion.MySql(new Version(8, 4, 11)));
        var generator = context.GetService<IMigrationsSqlGenerator>();
        MigrationOperation[] operations =
        [
            new AddColumnOperation
            {
                Table = "Entries",
                Name = "RecordedOn",
                ClrType = typeof(DateOnly),
                IsNullable = false,
                DefaultValue = new DateOnly(2026, 8, 17),
            },
            new AddColumnOperation
            {
                Table = "Entries",
                Name = "Payload",
                ClrType = typeof(string),
                ColumnType = "longtext CHARACTER SET utf8mb4",
                IsNullable = false,
                DefaultValue = "{}",
            },
        ];

        var sql = JoinSql(generator.Generate(operations, context.Model));

        Assert.Contains("DEFAULT (DATE '2026-08-17')", sql, StringComparison.Ordinal);
        Assert.Contains("DEFAULT ('{}')", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that MariaDB JSON alias columns preserve every legal metadata
    /// group that the special renderer owns.
    /// </summary>
    [Fact]
    public void MariaDb_json_alias_preserves_default_comment_and_invisible_metadata()
    {
        using var context = CreateContext(MySqlServerVersion.MariaDb(new Version(11, 8, 8)));
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AddColumnOperation
        {
            Table = "Entries",
            Name = "Payload",
            ClrType = typeof(string),
            ColumnType = "json",
            IsNullable = false,
            DefaultValue = "{}",
            Comment = "json metadata",
        };

        operation.SetAnnotation(MySqlAnnotationNames.Invisible, true);

        var sql = JoinSql(generator.Generate([operation], context.Model));

        Assert.Contains("`Payload` longtext COLLATE utf8mb4_bin NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("DEFAULT ('{}')", sql, StringComparison.Ordinal);
        Assert.Contains("CHECK (JSON_VALID(`Payload`))", sql, StringComparison.Ordinal);
        Assert.Contains("COMMENT 'json metadata' INVISIBLE", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that spatial columns preserve expression defaults, comments,
    /// invisible metadata, and emulated SRID checks.
    /// </summary>
    [Fact]
    public void Spatial_column_preserves_default_comment_and_invisible_metadata()
    {
        using var context = CreateContext(MySqlServerVersion.MariaDb(new Version(11, 8, 8)));
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AddColumnOperation
        {
            Table = "Entries",
            Name = "Location",
            ClrType = typeof(byte[]),
            ColumnType = "point",
            IsNullable = false,
            DefaultValueSql = "ST_GeomFromText('POINT(1 2)', 4326)",
            Comment = "spatial metadata",
        };

        operation.SetAnnotation(MySqlAnnotationNames.SpatialReferenceSystemId, 4326);
        operation.SetAnnotation(MySqlAnnotationNames.Invisible, true);

        var sql = JoinSql(generator.Generate([operation], context.Model));

        Assert.Contains("`Location` point NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("DEFAULT (ST_GeomFromText('POINT(1 2)', 4326))", sql, StringComparison.Ordinal);
        Assert.Contains("CHECK (ST_SRID(`Location`) = 4326)", sql, StringComparison.Ordinal);
        Assert.Contains("COMMENT 'spatial metadata' INVISIBLE", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that computed JSON and spatial columns use the generated-column
    /// renderer instead of losing their expression in a special type path.
    /// </summary>
    [Theory]
    [InlineData("json", "JSON_OBJECT('state', 'ready')")]
    [InlineData("point", "ST_GeomFromText('POINT(1 2)', 4326)")]
    public void Special_columns_preserve_computed_expressions_and_storage(
        string columnType,
        string computedColumnSql
    )
    {
        using var context = CreateContext(MySqlServerVersion.MariaDb(new Version(11, 8, 8)));
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AddColumnOperation
        {
            Table = "Entries",
            Name = "ComputedValue",
            ClrType = columnType == "json" ? typeof(string) : typeof(byte[]),
            ColumnType = columnType,
            IsNullable = true,
            ComputedColumnSql = computedColumnSql,
            IsStored = true,
            Comment = "computed metadata",
        };

        var sql = JoinSql(generator.Generate([operation], context.Model));

        Assert.Contains($"GENERATED ALWAYS AS ({computedColumnSql}) STORED", sql, StringComparison.Ordinal);
        Assert.Contains("COMMENT 'computed metadata'", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the MariaDB JSON alias path retains caller-authored
    /// default SQL without interpreting its contents.
    /// </summary>
    [Fact]
    public void MariaDb_json_alias_preserves_default_sql()
    {
        using var context = CreateContext(MySqlServerVersion.MariaDb(new Version(11, 8, 8)));
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AddColumnOperation
        {
            Table = "Entries",
            Name = "Payload",
            ClrType = typeof(string),
            ColumnType = "json",
            IsNullable = false,
            DefaultValueSql = "JSON_OBJECT('state', 'ready')",
        };

        var sql = JoinSql(generator.Generate([operation], context.Model));

        Assert.Contains("DEFAULT (JSON_OBJECT('state', 'ready'))", sql, StringComparison.Ordinal);
        Assert.Contains("CHECK (JSON_VALID(`Payload`))", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies MySQL's distinct invisible-column and comment attribute order.
    /// </summary>
    [Fact]
    public void MySql_places_invisible_before_comment()
    {
        using var context = CreateContext(MySqlServerVersion.MySql(new Version(8, 4, 11)));
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AddColumnOperation
        {
            Table = "Entries",
            Name = "Payload",
            ClrType = typeof(string),
            ColumnType = "varchar(64)",
            IsNullable = true,
            Comment = "metadata",
        };

        operation.SetAnnotation(MySqlAnnotationNames.Invisible, true);

        var sql = JoinSql(generator.Generate([operation], context.Model));

        Assert.Contains("INVISIBLE COMMENT 'metadata'", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that mode-sensitive caller SQL fails before any command can be
    /// returned when a backslash comment requires NO_BACKSLASH_ESCAPES.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Backslash_comment_rejects_mode_sensitive_caller_sql(
        bool useComputedSql
    )
    {
        using var context = CreateContext(MySqlServerVersion.MySql(new Version(8, 4, 11)));
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AddColumnOperation
        {
            Table = "Entries",
            Name = "Value",
            ClrType = typeof(string),
            ColumnType = "varchar(64)",
            IsNullable = true,
            Comment = "path\\segment",
            ComputedColumnSql = useComputedSql ? @"REPLACE(`Source`, '\\', '/')" : null,
            DefaultValueSql = useComputedSql ? null : @"'path\\segment'",
        };

        var exception = Assert.Throws<InvalidOperationException>(() => generator.Generate([operation], context.Model));

        Assert.Contains("NO_BACKSLASH_ESCAPES", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that mode-sensitive SQL anywhere in a create-table command is
    /// rejected when another object in that command activates the comment
    /// scope.
    /// </summary>
    [Theory]
    [InlineData("table_comment")]
    [InlineData("sibling_column_comment")]
    [InlineData("check_constraint")]
    public void Create_table_validates_mode_sensitive_sql_across_the_complete_command(
        string source
    )
    {
        using var context = CreateContext(MySqlServerVersion.MySql(new Version(8, 4, 11)));
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new CreateTableOperation
        {
            Name = "Entries",
            Comment = source == "table_comment" ? "path\\segment" : null,
        };

        operation.Columns.Add(
            new AddColumnOperation
            {
                Table = operation.Name,
                Name = "Commented",
                ClrType = typeof(string),
                ColumnType = "varchar(64)",
                IsNullable = true,
                Comment = source == "sibling_column_comment" ? "path\\segment" : null,
            });
        operation.Columns.Add(
            new AddColumnOperation
            {
                Table = operation.Name,
                Name = "ExpressionValue",
                ClrType = typeof(string),
                ColumnType = "varchar(64)",
                IsNullable = true,
                DefaultValueSql = source == "check_constraint" ? null : "'path\\\\segment'",
            });

        if (source == "check_constraint")
        {
            operation.Comment = "path\\segment";
            operation.CheckConstraints.Add(
                new AddCheckConstraintOperation
                {
                    Name = "CK_Entries_Path",
                    Table = operation.Name,
                    Sql = @"`ExpressionValue` <> 'path\\segment'",
                });
        }

        var exception = Assert.Throws<InvalidOperationException>(() => generator.Generate([operation], context.Model));

        Assert.Contains("backslash", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<MigrationOperation> CreateTemporalDefaultOperations(
        string path
    )
    {
        var date = CreateColumn<AddColumnOperation>("RecordedOn", typeof(DateOnly), "date", new DateOnly(2026, 8, 17));

        var time = CreateColumn<AddColumnOperation>(
            "RecordedAt",
            typeof(TimeOnly),
            "time(6)",
            new TimeOnly(12, 34, 56).Add(TimeSpan.FromTicks(1_234_567)));

        return path switch
        {
            "create" => [CreateTable(date, time)],
            "add" =>
            [
                date,
                time,
            ],
            "alter" =>
            [
                CreateAlterColumn(date),
                CreateAlterColumn(time),
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(path), path, "Unknown temporal default path."),
        };
    }

    private static TOperation CreateColumn<TOperation>(
        string name,
        Type clrType,
        string columnType,
        object defaultValue
    )
        where TOperation : ColumnOperation, new()
    {
        return new TOperation
        {
            Table = "Entries",
            Name = name,
            ClrType = clrType,
            ColumnType = columnType,
            IsNullable = false,
            DefaultValue = defaultValue,
        };
    }

    private static CreateTableOperation CreateTable(
        params AddColumnOperation[] columns
    )
    {
        var operation = new CreateTableOperation { Name = "Entries" };

        foreach (var column in columns)
        {
            operation.Columns.Add(column);
        }

        return operation;
    }

    private static AlterColumnOperation CreateAlterColumn(
        AddColumnOperation source
    )
    {
        return new AlterColumnOperation
        {
            Table = source.Table,
            Name = source.Name,
            ClrType = source.ClrType,
            ColumnType = source.ColumnType,
            IsNullable = source.IsNullable,
            DefaultValue = source.DefaultValue,
            OldColumn =
            {
                ClrType = source.ClrType,
                ColumnType = source.ColumnType,
                IsNullable = source.IsNullable,
            },
        };
    }

    private static DefaultContext CreateContext(
        MySqlServerVersion serverVersion
    )
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<DefaultContext>();
        builder.UseMySql("Server=localhost;Database=doka;User ID=root;Password=password;", serverVersion);

        return new DefaultContext(builder.Options);
    }

    private static string JoinSql(
        IReadOnlyList<MigrationCommand> commands
    ) => string.Join(Environment.NewLine, commands.Select(static command => command.CommandText));

    private sealed class DefaultContext : DbContext
    {
        public DefaultContext(
            DbContextOptions<DefaultContext> options
        ) : base(options) { }
    }
}
