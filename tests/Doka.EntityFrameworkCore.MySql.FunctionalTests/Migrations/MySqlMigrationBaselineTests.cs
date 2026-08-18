namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies the migrations, schema-qualification, and identifier-quoting baseline.
/// </summary>
public sealed class MySqlMigrationBaselineTests
{
    /// <summary>
    /// Verifies that the runtime connection path creates the expected MySQL connection type.
    /// </summary>
    [Fact]
    public void Relational_connection_uses_a_mysql_connection_for_connection_string_registration()
    {
        using var context = new BaselineContext(CreateOptions());

        var relationalConnection = context.GetService<IRelationalConnection>();

        Assert.IsType<MySqlConnection>(relationalConnection.DbConnection);
        Assert.Contains("Database=doka", relationalConnection.DbConnection.ConnectionString, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the baseline query translation uses MySQL limit syntax and parameterized predicates.
    /// </summary>
    [Fact]
    public void Query_translation_uses_limit_syntax_and_parameterized_predicates()
    {
        using var context = new BaselineContext(CreateOptions());
        var id = 7;

        var sql = context
            .Entities.Where(entity => entity.Id == id)
            .Take(2)
            .ToQueryString();

        Assert.Contains("-- @id='7'", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE `e`.`Id` = @id", sql, StringComparison.Ordinal);
        Assert.Contains("LIMIT @p", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("FETCH FIRST", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(" = 7", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the history repository generates the deterministic contract.
    /// </summary>
    [Fact]
    public void History_repository_generates_the_expected_phase_1_scripts()
    {
        using var context = new BaselineContext(CreateOptions());
        var historyRepository = context.GetService<IHistoryRepository>();
        var row = new HistoryRow("20260407120000_Initial", "10.0.0");

        var createScript = historyRepository.GetCreateScript();
        var insertScript = historyRepository.GetInsertScript(row);
        var deleteScript = historyRepository.GetDeleteScript(row.MigrationId);

        Assert.Contains("CREATE TABLE `__EFMigrationsHistory`", createScript, StringComparison.Ordinal);
        Assert.Contains("`MigrationId` varchar(150) NOT NULL", createScript, StringComparison.Ordinal);
        Assert.Contains("`ProductVersion` varchar(32) NOT NULL", createScript, StringComparison.Ordinal);
        Assert.Contains("CHARACTER SET utf8mb4", createScript, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO `__EFMigrationsHistory`", insertScript, StringComparison.Ordinal);
        Assert.Contains("'20260407120000_Initial'", insertScript, StringComparison.Ordinal);
        Assert.Contains("'10.0.0'", insertScript, StringComparison.Ordinal);
        Assert.Contains("DELETE FROM `__EFMigrationsHistory`", deleteScript, StringComparison.Ordinal);
        Assert.Contains("WHERE `MigrationId` = '20260407120000_Initial';", deleteScript, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the configured history-table name flows into generated baseline scripts.
    /// </summary>
    [Fact]
    public void History_repository_uses_the_configured_history_table_name()
    {
        var options = CreateOptions(extension =>
            (MySqlOptionsExtension)extension.WithMigrationsHistoryTableName("__CustomHistory"));

        using var context = new BaselineContext(options);
        var historyRepository = context.GetService<IHistoryRepository>();

        var createScript = historyRepository.GetCreateScript();

        Assert.Contains("CREATE TABLE `__CustomHistory`", createScript, StringComparison.Ordinal);
        Assert.Contains("CONSTRAINT `PK___CustomHistory` PRIMARY KEY", createScript, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the history repository rejects schema overrides for the migrations history table.
    /// </summary>
    [Fact]
    public void History_repository_rejects_migrations_history_schema_overrides()
    {
        var sink = new TestLogSink();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestLoggerProvider(sink)));
        var options = CreateOptions(
            extension => (MySqlOptionsExtension)extension
                .WithMigrationsHistoryTableName("__CustomHistory")
                .WithMigrationsHistoryTableSchema("dbo"),
            loggerFactory);

        using var context = new BaselineContext(options);

        var exception = Assert.Throws<InvalidOperationException>(() => context.GetService<IHistoryRepository>());
        var entry = Assert.Single(sink.Entries, entry => entry.EventId.Id == MySqlEventId.SchemaUnsupported.Id);

        Assert.Contains("migrations history table schema", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(MySqlEventId.SchemaUnsupported.Id, entry.EventId.Id);
        Assert.Equal(LogLevel.Error, entry.LogLevel);
        Assert.Equal(MySqlLoggerCategory.Configuration, entry.Category);
        Assert.Contains("migrations history table schema", entry.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dbo", entry.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the minimal migrations path can generate quoted baseline SQL commands.
    /// </summary>
    [Fact]
    public void Migrations_sql_generator_generates_quoted_create_table_sql()
    {
        using var context = new BaselineContext(CreateOptions());
        var migrationsSqlGenerator = context.GetService<IMigrationsSqlGenerator>();
        var commands = migrationsSqlGenerator.Generate(
            new MigrationOperation[]
            {
                new CreateTableOperation
                {
                    Name = "ProbeTable",
                    Columns =
                    {
                        new AddColumnOperation
                        {
                            Name = "Id",
                            ClrType = typeof(int),
                            ColumnType = "int",
                            IsNullable = false,
                        },
                    },
                },
            },
            context.Model);

        var command = Assert.Single(commands);

        Assert.Contains("CREATE TABLE `ProbeTable`", command.CommandText, StringComparison.Ordinal);
        Assert.Contains("`Id` int NOT NULL", command.CommandText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that EF schemas become MySQL database qualifiers for both tables
    /// and foreign-key principals.
    /// </summary>
    [Fact]
    public void Create_table_foreign_key_preserves_relational_schemas()
    {
        using var context = new BaselineContext(CreateOptions());
        var migrationsSqlGenerator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new CreateTableOperation
        {
            Name = "Child",
            Schema = "dbo2",
            Columns =
            {
                new AddColumnOperation
                {
                    Name = "ParentId",
                    ClrType = typeof(int),
                    ColumnType = "int",
                    IsNullable = false,
                },
            },
            ForeignKeys =
            {
                new AddForeignKeyOperation
                {
                    Name = "FK_Child_Parent",
                    Table = "Child",
                    Schema = "dbo2",
                    Columns = ["ParentId"],
                    PrincipalTable = "Parent",
                    PrincipalSchema = "dbo2",
                    PrincipalColumns = ["Id"],
                },
            },
        };

        var command = Assert.Single(
            migrationsSqlGenerator.Generate([operation], context.Model));

        Assert.Contains("CREATE TABLE `dbo2`.`Child`", command.CommandText, StringComparison.Ordinal);
        Assert.Contains("REFERENCES `dbo2`.`Parent` (`Id`)", command.CommandText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that EnsureSchema creates the equivalent MySQL database.
    /// </summary>
    [Fact]
    public void Ensure_schema_creates_database_if_missing()
    {
        using var context = new BaselineContext(CreateOptions());
        var migrationsSqlGenerator = context.GetService<IMigrationsSqlGenerator>();

        var command = Assert.Single(
            migrationsSqlGenerator.Generate([new EnsureSchemaOperation { Name = "tenant_database" }], context.Model));

        Assert.Equal(
            "CREATE DATABASE IF NOT EXISTS `tenant_database`;" + Environment.NewLine,
            command.CommandText);
    }

    /// <summary>
    /// Verifies that the provider migrations pipeline preserves the auto-increment contract for integer keys.
    /// </summary>
    [Fact]
    public void Migrations_pipeline_emits_auto_increment_for_integer_primary_keys()
    {
        using var sourceContext = new EmptyBaselineContext(CreateOptions<EmptyBaselineContext>());
        using var targetContext = new BaselineContext(CreateOptions<BaselineContext>());
        var differ = targetContext.GetService<IMigrationsModelDiffer>();
        var migrationsSqlGenerator = targetContext.GetService<IMigrationsSqlGenerator>();
        var operations = differ.GetDifferences(
            sourceContext
                .GetService<IDesignTimeModel>()
                .Model.GetRelationalModel(),
            targetContext
                .GetService<IDesignTimeModel>()
                .Model.GetRelationalModel());

        var createTable = Assert.Single(operations.OfType<CreateTableOperation>());
        var idColumn = Assert.Single(createTable.Columns, column => column.Name == nameof(BaselineEntity.Id));
        var commands = migrationsSqlGenerator.Generate(operations, targetContext.Model);
        var sql = string.Join(Environment.NewLine, commands.Select(command => command.CommandText));

        Assert.Equal(
            MySqlValueGenerationStrategy.AutoIncrement,
            idColumn.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy)
                ?.Value);
        Assert.Contains("`Id` int NOT NULL AUTO_INCREMENT", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that bounded string and binary columns emit valid MySQL store types with explicit sizes.
    /// </summary>
    [Fact]
    public void Migrations_sql_generator_emits_sizes_for_bounded_string_and_binary_columns()
    {
        using var context = new BaselineContext(CreateOptions());
        var migrationsSqlGenerator = context.GetService<IMigrationsSqlGenerator>();
        var commands = migrationsSqlGenerator.Generate(
            new MigrationOperation[]
            {
                new CreateTableOperation
                {
                    Name = "SizedColumns",
                    Columns =
                    {
                        new AddColumnOperation
                        {
                            Name = "Code",
                            ClrType = typeof(string),
                            MaxLength = 32,
                            IsNullable = false,
                        },
                        new AddColumnOperation
                        {
                            Name = "LegacyGuid",
                            ClrType = typeof(string),
                            IsFixedLength = true,
                            MaxLength = 36,
                            IsNullable = false,
                        },
                        new AddColumnOperation
                        {
                            Name = "Token",
                            ClrType = typeof(byte[]),
                            MaxLength = 64,
                            IsNullable = false,
                        },
                    },
                },
            },
            context.Model);

        var command = Assert.Single(commands);

        Assert.Contains("`Code` varchar(32) NOT NULL", command.CommandText, StringComparison.Ordinal);
        Assert.Contains("`LegacyGuid` char(36) NOT NULL", command.CommandText, StringComparison.Ordinal);
        Assert.Contains("`Token` varbinary(64) NOT NULL", command.CommandText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that default-schema configuration is preserved as a database qualifier.
    /// </summary>
    [Fact]
    public void Default_schema_configuration_is_preserved()
    {
        using var context = new DefaultSchemaContext(CreateOptions());

        Assert.Equal("dbo", context.Model.GetDefaultSchema());
    }

    /// <summary>
    /// Verifies that entity-schema configuration is preserved as a database qualifier.
    /// </summary>
    [Fact]
    public void Entity_schema_configuration_is_preserved()
    {
        using var context = new EntitySchemaContext(CreateOptions());
        var entityType = context.Model.FindEntityType(typeof(BaselineEntity));

        Assert.NotNull(entityType);
        Assert.Equal("dbo", entityType.GetSchema());
    }

    [Fact]
    public void Create_index_emits_desc_for_descending_columns()
    {
        using var context = new BaselineContext(CreateOptions());
        var migrationsSqlGenerator = context.GetService<IMigrationsSqlGenerator>();
        var commands = migrationsSqlGenerator.Generate(
            new MigrationOperation[]
            {
                new CreateIndexOperation
                {
                    Name = "IX_DescTest",
                    Table = "DescTestTable",
                    Columns = ["CreatedAt"],
                    IsDescending = [true],
                },
            },
            context.Model);

        var command = Assert.Single(commands);
        Assert.Contains("DESC", command.CommandText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that an EF schema qualifies an index target with its MySQL database.
    /// </summary>
    [Fact]
    public void Create_index_preserves_relational_schema()
    {
        using var context = new BaselineContext(CreateOptions());
        var migrationsSqlGenerator = context.GetService<IMigrationsSqlGenerator>();
        var commands = migrationsSqlGenerator.Generate(
            new MigrationOperation[]
            {
                new CreateIndexOperation
                {
                    Name = "IX_SchemaProbe_Code",
                    Table = "SchemaProbe",
                    Schema = "dbo2",
                    Columns = ["Code"],
                },
            },
            context.Model);

        var command = Assert.Single(commands);

        Assert.Contains("ON `dbo2`.`SchemaProbe` (`Code`)", command.CommandText, StringComparison.Ordinal);
    }

    /// <summary>
    /// RENAME TABLE produces MySQL-specific syntax.
    /// </summary>
    [Fact]
    public void Rename_table_produces_mysql_rename_syntax()
    {
        using var context = new BaselineContext(CreateOptions());
        var migrationsSqlGenerator = context.GetService<IMigrationsSqlGenerator>();
        var commands = migrationsSqlGenerator.Generate(
            new MigrationOperation[]
            {
                new RenameTableOperation
                {
                    Name = "OldTable",
                    Schema = "source_database",
                    NewName = "NewTable",
                    NewSchema = "target_database",
                },
            },
            context.Model);

        var command = Assert.Single(commands);
        Assert.Contains("RENAME TABLE", command.CommandText, StringComparison.Ordinal);
        Assert.Contains("`source_database`.`OldTable`", command.CommandText, StringComparison.Ordinal);
        Assert.Contains("`target_database`.`NewTable`", command.CommandText, StringComparison.Ordinal);
    }

    /// <summary>
    /// RENAME COLUMN produces ALTER TABLE ... RENAME COLUMN syntax.
    /// </summary>
    [Fact]
    public void Rename_column_produces_mysql_rename_column_syntax()
    {
        using var context = new BaselineContext(CreateOptions());
        var migrationsSqlGenerator = context.GetService<IMigrationsSqlGenerator>();
        var commands = migrationsSqlGenerator.Generate(
            new MigrationOperation[]
            {
                new RenameColumnOperation
                {
                    Table = "TestTable",
                    Name = "OldColumn",
                    NewName = "NewColumn",
                },
            },
            context.Model);

        var command = Assert.Single(commands);
        Assert.Contains("RENAME COLUMN", command.CommandText, StringComparison.Ordinal);
        Assert.Contains("`OldColumn`", command.CommandText, StringComparison.Ordinal);
        Assert.Contains("`NewColumn`", command.CommandText, StringComparison.Ordinal);
    }

    /// <summary>
    /// CreateSequenceOperation produces correct DDL.
    /// </summary>
    [Fact]
    public void Create_sequence_operation_produces_table_emulation_ddl()
    {
        using var context = new BaselineContext(CreateOptions());
        var migrationsSqlGenerator = context.GetService<IMigrationsSqlGenerator>();
        var commands = migrationsSqlGenerator.Generate(
            new MigrationOperation[]
            {
                new CreateSequenceOperation
                {
                    Name = "TestSequence",
                    StartValue = 100,
                    IncrementBy = 1,
                },
            },
            context.Model);

        // MySQL path: should create an emulation table.
        var sql = string.Join(Environment.NewLine, commands.Select(c => c.CommandText));

        Assert.Contains("__efsequence_TestSequence", sql, StringComparison.Ordinal);
        Assert.Contains("`value` BIGINT NOT NULL", sql, StringComparison.Ordinal);
    }

    private static DbContextOptions<TContext> CreateOptions<TContext>(
        Func<MySqlOptionsExtension, MySqlOptionsExtension>? configureExtension = null,
        ILoggerFactory? loggerFactory = null
    )
        where TContext : DbContext
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<TContext>();

        if (loggerFactory is not null)
        {
            builder.UseLoggerFactory(loggerFactory);
        }

        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        if (configureExtension is not null)
        {
            var extension =
                Assert.IsType<MySqlOptionsExtension>(builder.Options.FindExtension<MySqlOptionsExtension>());

            extension = configureExtension(extension);
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(extension);
        }

        return builder.Options;
    }

    private static DbContextOptions<BaselineContext> CreateOptions(
        Func<MySqlOptionsExtension, MySqlOptionsExtension>? configureExtension = null,
        ILoggerFactory? loggerFactory = null
    ) => CreateOptions<BaselineContext>(configureExtension, loggerFactory);

    private class BaselineContext : DbContext
    {
        public BaselineContext(
            DbContextOptions options
        ) : base(options) { }

        public DbSet<BaselineEntity> Entities => Set<BaselineEntity>();
    }

    private sealed class EmptyBaselineContext : DbContext
    {
        public EmptyBaselineContext(
            DbContextOptions options
        ) : base(options) { }
    }

    private sealed class DefaultSchemaContext : BaselineContext
    {
        public DefaultSchemaContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.HasDefaultSchema("dbo");
    }

    private sealed class EntitySchemaContext : BaselineContext
    {
        public EntitySchemaContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder
                .Entity<BaselineEntity>()
                .ToTable("BaselineEntities", "dbo");
        }
    }

    private sealed class BaselineEntity
    {
        public int Id { get; set; }
    }
}
