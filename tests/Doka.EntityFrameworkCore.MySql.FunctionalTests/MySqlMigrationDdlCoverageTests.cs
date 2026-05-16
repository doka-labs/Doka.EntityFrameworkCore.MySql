namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Coverage tests for migration DDL operations: Rename Table/Column, AlterSequence,
/// Spatial Index, INVISIBLE, and argument validation.
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
    public void AlterSequence_mysql_generates_comment()
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

        // MySQL emulation: increment changes are applied at fetch time.
        Assert.Contains("--", sql, StringComparison.Ordinal);
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

    // -- ModelValidator: constraint name length --

    [Fact]
    public void Model_with_long_fk_name_builds_and_validator_rejects()
    {
        var builder = new DbContextOptionsBuilder<ConstraintTestContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        // Context construction triggers model finalization and validation.
        // The FK constraint name exceeds 64 chars.
        var exception = Assert.ThrowsAny<Exception>(() =>
        {
            using var context = new ConstraintTestContext(builder.Options);
            // Force model build by accessing it.
            _ = context.Model;
        });

        Assert.Contains("64", exception.Message, StringComparison.Ordinal);
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
        var property = context.Model.FindEntityType(typeof(HiLoShortEntity))!
            .FindProperty(nameof(HiLoShortEntity.Id))!;

        Assert.Equal(MySqlValueGenerationStrategy.HiLo, property.GetMySqlValueGenerationStrategy());
    }

    // -- Helpers --

    private static string JoinSql(
        IReadOnlyList<MigrationCommand> commands
    )
        => string.Join("\n", commands.Select(c => c.CommandText));

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
        ) => modelBuilder.Entity<TemporalEntity>(e => { e.HasKey(x => x.Id); });
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

            // Create a FK with a very long constraint name (>64 chars).
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
