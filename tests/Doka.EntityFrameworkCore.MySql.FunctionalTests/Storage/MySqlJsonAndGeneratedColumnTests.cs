namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies the JSON and generated-column baseline.
/// </summary>
public sealed class MySqlJsonAndGeneratedColumnTests
{
    /// <summary>
    /// Verifies that JSON and generated-column metadata are preserved for the MySQL baseline.
    /// </summary>
    [Fact]
    public void MySql_model_metadata_preserves_json_and_generated_column_configuration()
    {
        using var context = new MySqlJsonContext(
            CreateOptions<MySqlJsonContext>(MySqlServerVersion.MySql(new Version(8, 4, 0))));
        var entityType = context.Model.FindEntityType(typeof(JsonEntity))!;
        var payload = entityType.FindProperty(nameof(JsonEntity.Payload))!;
        var virtualKind = entityType.FindProperty(nameof(JsonEntity.VirtualKind))!;
        var storedCount = entityType.FindProperty(nameof(JsonEntity.StoredCount))!;

        Assert.Equal("json", payload.GetColumnType());
        Assert.Equal("JSON_UNQUOTE(JSON_EXTRACT(`Payload`, '$.kind'))", virtualKind.GetComputedColumnSql());
        Assert.False(virtualKind.GetIsStored());
        Assert.Equal("JSON_LENGTH(`Payload`)", storedCount.GetComputedColumnSql());
        Assert.True(storedCount.GetIsStored());
    }

    /// <summary>
    /// Verifies that MySQL emits native JSON plus explicit virtual/stored generated-column SQL.
    /// </summary>
    [Fact]
    public void MySql_migrations_sql_generator_emits_native_json_and_generated_column_sql()
    {
        using var context = new MySqlJsonContext(
            CreateOptions<MySqlJsonContext>(MySqlServerVersion.MySql(new Version(8, 4, 0))));
        var sql = GenerateJsonCreateTableSql(context);

        Assert.Contains("`Payload` json NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains(
            "`VirtualKind` varchar(64) GENERATED ALWAYS AS (JSON_UNQUOTE(JSON_EXTRACT(`Payload`, '$.kind'))) VIRTUAL NOT NULL",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "`StoredCount` int GENERATED ALWAYS AS (JSON_LENGTH(`Payload`)) STORED NOT NULL",
            sql,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that MariaDB emits JSON alias DDL instead of pretending to have native binary JSON.
    /// </summary>
    [Fact]
    public void MariaDb_migrations_sql_generator_emits_json_alias_and_generated_column_sql()
    {
        using var context = new MariaDbJsonContext(
            CreateOptions<MariaDbJsonContext>(MySqlServerVersion.MariaDb(new Version(11, 8, 0))));
        var sql = GenerateJsonCreateTableSql(context);

        Assert.Contains(
            "`Payload` longtext COLLATE utf8mb4_bin NOT NULL CHECK (JSON_VALID(`Payload`))",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "`VirtualKind` varchar(64) GENERATED ALWAYS AS (JSON_UNQUOTE(JSON_EXTRACT(`Payload`, '$.kind'))) VIRTUAL",
            sql,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "`VirtualKind` varchar(64) GENERATED ALWAYS AS (JSON_UNQUOTE(JSON_EXTRACT(`Payload`, '$.kind'))) VIRTUAL NOT NULL",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "`StoredCount` int GENERATED ALWAYS AS (JSON_LENGTH(`Payload`)) STORED",
            sql,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "`StoredCount` int GENERATED ALWAYS AS (JSON_LENGTH(`Payload`)) STORED NOT NULL",
            sql,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that legacy MariaDB versions use the engine's PERSISTENT spelling
    /// instead of rejecting stored generated columns that the engine supports.
    /// </summary>
    [Fact]
    public void Legacy_mariadb_uses_persistent_generated_column_syntax()
    {
        using var context = new MariaDbJsonContext(
            CreateOptions<MariaDbJsonContext>(
                MySqlServerVersion.MariaDb(
                    new Version(10, 1, 0),
                    MySqlServerVersionCompatibilityMode.AllowUnsupported)));
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = CreateJsonTableOperation();

        operation.Columns.RemoveAt(0);

        var command = Assert.Single(generator.Generate([operation], context.Model));

        Assert.Contains(
            "`VirtualKind` varchar(64) GENERATED ALWAYS AS "
            + "(JSON_UNQUOTE(JSON_EXTRACT(`Payload`, '$.kind'))) VIRTUAL",
            command.CommandText,
            StringComparison.Ordinal);
        Assert.Contains(
            "`StoredCount` int GENERATED ALWAYS AS (JSON_LENGTH(`Payload`)) PERSISTENT",
            command.CommandText,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that MariaDB releases without JSON_VALID reject JSON columns
    /// instead of emitting an invalid validation constraint.
    /// </summary>
    [Fact]
    public void Legacy_mariadb_without_json_validation_rejects_json_columns()
    {
        using var context = new MariaDbJsonContext(
            CreateOptions<MariaDbJsonContext>(
                MySqlServerVersion.MariaDb(
                    new Version(10, 2, 2),
                    MySqlServerVersionCompatibilityMode.AllowUnsupported)));
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = CreateJsonTableOperation();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            generator.Generate([operation], context.Model));

        Assert.Contains("JSON", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that legacy MySQL versions fail explicitly instead of silently accepting unsupported JSON columns.
    /// </summary>
    [Fact]
    public void Legacy_mysql_rejects_native_json_columns_explicitly()
    {
        using var context = new MySqlJsonContext(
            CreateOptions<MySqlJsonContext>(
                MySqlServerVersion.MySql(
                    new Version(5, 6, 0),
                    MySqlServerVersionCompatibilityMode.AllowUnsupported)));
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = CreateJsonTableOperation();

        var exception = Assert.Throws<InvalidOperationException>(() => generator.Generate([operation], context.Model));

        Assert.Contains("native JSON", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that unsupported generated-column variants fail explicitly for legacy server versions.
    /// </summary>
    [Fact]
    public void Legacy_mysql_rejects_generated_columns_explicitly()
    {
        using var context = new MySqlJsonContext(
            CreateOptions<MySqlJsonContext>(
                MySqlServerVersion.MySql(
                    new Version(5, 6, 0),
                    MySqlServerVersionCompatibilityMode.AllowUnsupported)));
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = CreateJsonTableOperation();

        operation.Columns.RemoveAt(0);

        var exception = Assert.Throws<InvalidOperationException>(() => generator.Generate([operation], context.Model));

        Assert.Contains("generated columns", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that an unspecified generated-column variant uses the native
    /// virtual default instead of introducing a provider-only restriction.
    /// </summary>
    [Fact]
    public void Generated_columns_without_an_explicit_variant_are_virtual()
    {
        using var context = new MySqlJsonContext(
            CreateOptions<MySqlJsonContext>(MySqlServerVersion.MySql(new Version(8, 4, 0))));
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = CreateJsonTableOperation();

        operation.Columns[1].IsStored = null;

        var command = Assert.Single(generator.Generate([operation], context.Model));

        Assert.Contains(
            "`VirtualKind` varchar(64) GENERATED ALWAYS AS "
                + "(JSON_UNQUOTE(JSON_EXTRACT(`Payload`, '$.kind'))) VIRTUAL NOT NULL",
            command.CommandText,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string GenerateJsonCreateTableSql(
        DbContext context
    )
    {
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var command = Assert.Single(generator.Generate([CreateJsonTableOperation()], context.Model));

        return command.CommandText;
    }

    private static CreateTableOperation CreateJsonTableOperation()
    {
        return new CreateTableOperation
        {
            Name = "Phase2JsonEntities",
            Columns =
            {
                new AddColumnOperation
                {
                    Name = "Payload",
                    ClrType = typeof(string),
                    ColumnType = "json",
                    IsNullable = false,
                },
                new AddColumnOperation
                {
                    Name = "VirtualKind",
                    ClrType = typeof(string),
                    ColumnType = "varchar(64)",
                    ComputedColumnSql = "JSON_UNQUOTE(JSON_EXTRACT(`Payload`, '$.kind'))",
                    IsStored = false,
                    IsNullable = false,
                },
                new AddColumnOperation
                {
                    Name = "StoredCount",
                    ClrType = typeof(int),
                    ColumnType = "int",
                    ComputedColumnSql = "JSON_LENGTH(`Payload`)",
                    IsStored = true,
                    IsNullable = false,
                },
            },
        };
    }

    private static DbContextOptions<TContext> CreateOptions<TContext>(
        MySqlServerVersion serverVersion
    )
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();

        builder.UseMySql("Server=localhost;Database=doka;User ID=root;Password=password;", serverVersion);

        return builder.Options;
    }

    private sealed class MySqlJsonContext : DbContext
    {
        public MySqlJsonContext(
            DbContextOptions<MySqlJsonContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureJsonEntity(modelBuilder);
    }

    private sealed class MariaDbJsonContext : DbContext
    {
        public MariaDbJsonContext(
            DbContextOptions<MariaDbJsonContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureJsonEntity(modelBuilder);
    }

    private static void ConfigureJsonEntity(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<JsonEntity>(entity =>
        {
            entity.ToTable("Phase2JsonEntities");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id);
            entity
                .Property(item => item.Payload)
                .HasColumnType("json");
            entity
                .Property(item => item.VirtualKind)
                .HasColumnType("varchar(64)")
                .HasComputedColumnSql("JSON_UNQUOTE(JSON_EXTRACT(`Payload`, '$.kind'))", stored: false);
            entity
                .Property(item => item.StoredCount)
                .HasColumnType("int")
                .HasComputedColumnSql("JSON_LENGTH(`Payload`)", stored: true);
        });
    }

    private sealed class JsonEntity
    {
        public int Id { get; set; }

        public string Payload { get; set; } = string.Empty;

        public string VirtualKind { get; set; } = string.Empty;

        public int StoredCount { get; set; }
    }
}
