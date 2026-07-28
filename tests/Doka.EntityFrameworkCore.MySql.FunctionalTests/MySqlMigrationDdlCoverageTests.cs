namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Coverage tests for migration DDL operations: Rename Table/Column, AlterSequence,
/// Spatial Index, INVISIBLE, identifier normalization, and argument validation.
/// </summary>
public sealed class MySqlMigrationDdlCoverageTests
{
    // -- RENAME TABLE --

    [Fact]
    public void RenameTable_generates_rename_table_sql()
    {
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new RenameTableOperation
        {
            Name = "OldTable",
            NewName = "NewTable",
        };

        var commands = generator.Generate([operation], context.Model);
        var sql = JoinSql(commands);

        Assert.Contains("RENAME TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`OldTable`", sql, StringComparison.Ordinal);
        Assert.Contains("`NewTable`", sql, StringComparison.Ordinal);
    }

    // -- RENAME COLUMN --

    [Fact]
    public void RenameColumn_generates_alter_table_rename_column_sql()
    {
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new RenameColumnOperation
        {
            Table = "Products",
            Name = "OldCol",
            NewName = "NewCol",
        };

        var commands = generator.Generate([operation], context.Model);
        var sql = JoinSql(commands);

        Assert.Contains("ALTER TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RENAME COLUMN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`OldCol`", sql, StringComparison.Ordinal);
        Assert.Contains("`NewCol`", sql, StringComparison.Ordinal);
    }

    // -- ALTER SEQUENCE --

    [Fact]
    public void AlterSequence_mysql_updates_persisted_metadata()
    {
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AlterSequenceOperation
        {
            Name = "TestSeq",
            IncrementBy = 5,
        };

        var commands = generator.Generate([operation], context.Model);
        var sql = JoinSql(commands);

        Assert.Contains("UPDATE `__efsequence_TestSeq`", sql, StringComparison.Ordinal);
        Assert.Contains("`increment_by` = 5", sql, StringComparison.Ordinal);
        Assert.Contains("`is_cyclic` = FALSE", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void AlterSequence_mariadb_generates_alter_sequence_sql()
    {
        using var context = CreateMariaDbContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AlterSequenceOperation
        {
            Name = "TestSeq",
            IncrementBy = 5,
        };

        var commands = generator.Generate([operation], context.Model);
        var sql = JoinSql(commands);

        Assert.Contains("ALTER SEQUENCE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`TestSeq`", sql, StringComparison.Ordinal);
        Assert.Contains("INCREMENT BY 5", sql, StringComparison.OrdinalIgnoreCase);
    }

    // -- CREATE SEQUENCE quoting (emulation + native) --

    [Fact]
    public void CreateSequence_emulation_quotes_table_name_with_backtick_prefix()
    {
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new CreateSequenceOperation
        {
            Name = "OrderSeq",
            StartValue = 1,
            IncrementBy = 10,
            ClrType = typeof(long),
        };

        var sql = JoinSql(generator.Generate([operation], context.Model));

        Assert.Contains("CREATE TABLE `__efsequence_OrderSeq`", sql, StringComparison.Ordinal);
        Assert.Contains("`id` TINYINT UNSIGNED NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`value` BIGINT NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`start_value` BIGINT NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`increment_by` INT NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`min_value` BIGINT NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`max_value` BIGINT NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`is_cyclic` BOOLEAN NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`is_called` BOOLEAN NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("PRIMARY KEY (`id`)", sql, StringComparison.Ordinal);
        Assert.Contains("CHECK (`id` = 1)", sql, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO `__efsequence_OrderSeq`", sql, StringComparison.Ordinal);
        Assert.Contains(
            "VALUES (1, 1, 1, 10, 1, 9223372036854775806, FALSE, FALSE)",
            sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CreateSequence_emulation_doubles_embedded_backticks_in_table_name()
    {
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new CreateSequenceOperation
        {
            Name = "evil`seq",
            StartValue = 1,
            IncrementBy = 1,
            ClrType = typeof(long),
        };

        var sql = JoinSql(generator.Generate([operation], context.Model));

        Assert.Contains("`__efsequence_evil``seq`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("__efsequence_evil`seq`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateSequence_native_quotes_sequence_name_with_backticks()
    {
        using var context = CreateMariaDbContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new CreateSequenceOperation
        {
            Name = "OrderSeq",
            StartValue = 100,
            IncrementBy = 5,
            ClrType = typeof(long),
        };

        var sql = JoinSql(generator.Generate([operation], context.Model));

        Assert.Contains("CREATE SEQUENCE `OrderSeq`", sql, StringComparison.Ordinal);
        Assert.Contains("START WITH 100", sql, StringComparison.Ordinal);
        Assert.Contains("INCREMENT BY 5", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateSequence_native_doubles_embedded_backticks_in_sequence_name()
    {
        using var context = CreateMariaDbContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new CreateSequenceOperation
        {
            Name = "evil`seq",
            StartValue = 1,
            IncrementBy = 1,
            ClrType = typeof(long),
        };

        var sql = JoinSql(generator.Generate([operation], context.Model));

        Assert.Contains("CREATE SEQUENCE `evil``seq`", sql, StringComparison.Ordinal);
    }

    // -- DROP SEQUENCE quoting --

    [Fact]
    public void DropSequence_emulation_quotes_emulation_table_name()
    {
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new DropSequenceOperation { Name = "evil`seq" };

        var sql = JoinSql(generator.Generate([operation], context.Model));

        Assert.Contains("DROP TABLE IF EXISTS `__efsequence_evil``seq`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void DropSequence_native_quotes_sequence_name()
    {
        using var context = CreateMariaDbContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new DropSequenceOperation { Name = "evil`seq" };

        var sql = JoinSql(generator.Generate([operation], context.Model));

        Assert.Contains("DROP SEQUENCE IF EXISTS `evil``seq`", sql, StringComparison.Ordinal);
    }

    // -- RENAME SEQUENCE quoting --

    [Fact]
    public void RenameSequence_emulation_quotes_old_and_new_emulation_table_names()
    {
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new RenameSequenceOperation
        {
            Name = "OldSeq",
            NewName = "evil`new",
        };

        var sql = JoinSql(generator.Generate([operation], context.Model));

        Assert.Contains(
            "RENAME TABLE `__efsequence_OldSeq` TO `__efsequence_evil``new`",
            sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RenameSequence_native_renames_the_sequence_table()
    {
        using var context = CreateMariaDbContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new RenameSequenceOperation
        {
            Name = "OldSeq",
            NewName = "NewSeq",
        };

        var sql = JoinSql(generator.Generate([operation], context.Model));

        Assert.Contains("RENAME TABLE `OldSeq` TO `NewSeq`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void RestartSequence_native_updates_start_metadata_and_next_value()
    {
        using var context = CreateMariaDbContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new RestartSequenceOperation
        {
            Name = "TestSeq",
            StartValue = 3,
        };

        var sql = JoinSql(generator.Generate([operation], context.Model));

        Assert.Contains("START WITH 3 RESTART WITH 3", sql, StringComparison.Ordinal);
    }

    // -- SPATIAL INDEX --

    [Fact]
    public void CreateIndex_with_spatial_annotation_generates_spatial_index()
    {
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new CreateIndexOperation
        {
            Name = "IX_Location",
            Table = "Places",
            Columns = ["Location"],
        };
        operation.SetAnnotation(MySqlAnnotationNames.SpatialIndex, true);

        var commands = generator.Generate([operation], context.Model);
        var sql = JoinSql(commands);

        Assert.Contains("SPATIAL INDEX", sql, StringComparison.OrdinalIgnoreCase);
    }

    // -- JSON translation gaps --

    [Fact]
    public void JsonReplace_translates_to_json_replace_sql()
    {
        using var context = CreateJsonContext();
        var sql = context
            .Set<JsonEntity>()
            .Select(e => EF.Functions.JsonReplace(e.Data, "$.name", "updated"))
            .ToQueryString();

        Assert.Contains("JSON_REPLACE", sql, StringComparison.OrdinalIgnoreCase);
    }

    // -- TimeOnly.AddHours / AddMinutes --

    [Fact]
    public void TimeOnly_AddHours_translates_to_interval_hour()
    {
        using var context = CreateTemporalContext();
        var sql = context
            .Set<TemporalEntity>()
            .Where(e => e.StartTime.AddHours(2) > new TimeOnly(14, 0))
            .ToQueryString();

        Assert.Contains("INTERVAL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("HOUR", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TimeOnly_AddMinutes_translates_to_interval_minute()
    {
        using var context = CreateTemporalContext();
        var sql = context
            .Set<TemporalEntity>()
            .Where(e => e.StartTime.AddMinutes(30) > new TimeOnly(14, 0))
            .ToQueryString();

        Assert.Contains("INTERVAL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MINUTE", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that convention-generated foreign-key names are normalized to the
    /// cross-engine 64-character identifier limit before model validation.
    /// </summary>
    [Fact]
    public void Model_with_long_fk_name_builds_with_normalized_constraint_name()
    {
        var builder = new DbContextOptionsBuilder<ConstraintTestContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        using var context = new ConstraintTestContext(builder.Options);
        var childEntity = context.Model.FindEntityType(typeof(ChildEntity))
            ?? throw new InvalidOperationException("ChildEntity metadata was not created.");
        var foreignKey = Assert.Single(childEntity.GetForeignKeys());
        var constraintName = foreignKey.GetConstraintName()
            ?? throw new InvalidOperationException("Foreign-key constraint name was not generated.");

        Assert.True(
            constraintName.Length <= 64,
            $"Convention-generated constraint name '{constraintName}' exceeds 64 characters.");
    }

    // -- HiLo for short type --

    [Fact]
    public void UseHiLo_on_short_property_sets_strategy()
    {
        var builder = new DbContextOptionsBuilder<HiLoShortContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        using var context = new HiLoShortContext(builder.Options);
        var property = context.Model.FindEntityType(typeof(HiLoShortEntity))!.FindProperty(nameof(HiLoShortEntity.Id))!;

        Assert.Equal(MySqlValueGenerationStrategy.HiLo, property.GetMySqlValueGenerationStrategy());
    }

    // -- Helpers --

    private static string JoinSql(
        IReadOnlyList<MigrationCommand> commands
    ) => string.Join("\n", commands.Select(c => c.CommandText));

    private static DdlCoverageContext CreateMySqlContext()
    {
        var builder = new DbContextOptionsBuilder<DdlCoverageContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));
        return new DdlCoverageContext(builder.Options);
    }

    private static DdlCoverageContext CreateMariaDbContext()
    {
        var builder = new DbContextOptionsBuilder<DdlCoverageContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MariaDb(new Version(11, 8, 0)));
        return new DdlCoverageContext(builder.Options);
    }

    private static JsonFuncContext CreateJsonContext()
    {
        var builder = new DbContextOptionsBuilder<JsonFuncContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));
        return new JsonFuncContext(builder.Options);
    }

    private static TemporalContext CreateTemporalContext()
    {
        var builder = new DbContextOptionsBuilder<TemporalContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));
        return new TemporalContext(builder.Options);
    }

    // -- Entities / Contexts --

    private sealed class DdlCoverageContext : DbContext
    {
        public DdlCoverageContext(
            DbContextOptions<DdlCoverageContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<DdlEntity>(e => { e.HasKey(x => x.Id); });
    }

    private sealed class DdlEntity
    {
        public int Id { get; set; }
    }

    private sealed class JsonFuncContext : DbContext
    {
        public JsonFuncContext(
            DbContextOptions<JsonFuncContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<JsonEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.Data)
                    .HasColumnType("json");
            });
        }
    }

    private sealed class JsonEntity
    {
        public int Id { get; set; }
        public string Data { get; set; } = "{}";
    }

    private sealed class TemporalContext : DbContext
    {
        public TemporalContext(
            DbContextOptions<TemporalContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<TemporalEntity>(e => e.HasKey(x => x.Id));
    }

    private sealed class TemporalEntity
    {
        public int Id { get; set; }
        public TimeOnly StartTime { get; set; }
    }

    private sealed class ConstraintTestContext : DbContext
    {
        public ConstraintTestContext(
            DbContextOptions<ConstraintTestContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<ParentEntity>(e =>
            {
                e.ToTable("Parents");
                e.HasKey(x => x.Id);
            });

            // The long table name makes the convention-generated foreign-key name exceed
            // the shared MySQL/MariaDB identifier limit before normalization.
            modelBuilder.Entity<ChildEntity>(e =>
            {
                e.ToTable("ChildrenWithVeryLongTableNameThatExceedsTheSixtyFourCharacterLimit");
                e.HasKey(x => x.Id);
                e
                    .HasOne<ParentEntity>()
                    .WithMany()
                    .HasForeignKey(x => x.ParentId);
            });
        }
    }

    private sealed class ParentEntity
    {
        public int Id { get; set; }
    }

    private sealed class ChildEntity
    {
        public int Id { get; set; }
        public int ParentId { get; set; }
    }

    private sealed class HiLoShortContext : DbContext
    {
        public HiLoShortContext(
            DbContextOptions<HiLoShortContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<HiLoShortEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.Id)
                    .UseHiLo("ShortSeq");
            });
        }
    }

    private sealed class HiLoShortEntity
    {
        public short Id { get; set; }
    }
}
