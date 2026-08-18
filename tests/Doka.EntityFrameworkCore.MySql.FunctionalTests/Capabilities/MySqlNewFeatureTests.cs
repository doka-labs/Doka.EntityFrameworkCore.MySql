namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Tests for newly implemented features: HiLo, DateOnly/TimeOnly arithmetic,
/// INVISIBLE columns, and JSON functions.
/// </summary>
public sealed class MySqlNewFeatureTests
{
    // -- HiLo Value Generation --

    /// <summary>
    /// UseHiLo creates a sequence in the model and sets the HiLo value generation strategy.
    /// </summary>
    [Fact]
    public void UseHiLo_creates_sequence_and_sets_strategy()
    {
        using var context = CreateContext<HiLoContext>();
        var property = context.Model.FindEntityType(typeof(HiLoEntity))!.FindProperty(nameof(HiLoEntity.Id))!;

        Assert.Equal(MySqlValueGenerationStrategy.HiLo, property.GetMySqlValueGenerationStrategy());
        Assert.Equal(ValueGenerated.OnAdd, property.ValueGenerated);

        var sequence = context.Model.FindSequence("HiLoTestSequence");
        Assert.NotNull(sequence);
        Assert.Equal(10, sequence.IncrementBy);
    }

    /// <summary>
    /// UseHiLo with default name creates a convention-based sequence name.
    /// </summary>
    [Fact]
    public void UseHiLo_with_default_name_creates_convention_sequence()
    {
        using var context = CreateContext<HiLoDefaultContext>();
        var property =
            context.Model.FindEntityType(typeof(HiLoDefaultEntity))!.FindProperty(nameof(HiLoDefaultEntity.Id))!;

        Assert.Equal(MySqlValueGenerationStrategy.HiLo, property.GetMySqlValueGenerationStrategy());

        var sequences = context
            .Model.GetSequences()
            .ToList();
        Assert.Single(sequences);
        Assert.Contains("HiLoDefaultEntity", sequences[0].Name, StringComparison.Ordinal);
    }

    /// <summary>
    /// HiLo sequence generates the correct DDL for MySQL (table-based emulation).
    /// </summary>
    [Fact]
    public void HiLo_sequence_generates_mysql_table_based_ddl()
    {
        using var context = CreateContext<HiLoContext>();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new CreateSequenceOperation
        {
            Name = "HiLoTestSequence",
            StartValue = 1,
            IncrementBy = 10,
            ClrType = typeof(long),
        };

        var commands = generator.Generate([operation], context.Model);
        var sql = string.Join("\n", commands.Select(c => c.CommandText));

        Assert.Contains("__efsequence_HiLoTestSequence", sql, StringComparison.Ordinal);
    }

    // -- DateOnly/TimeOnly Arithmetic --

    /// <summary>
    /// DateOnly.AddDays translates to DATE_ADD with INTERVAL DAY.
    /// </summary>
    [Fact]
    public void DateOnly_AddDays_translates_to_date_add()
    {
        using var context = CreateContext<DateTimeContext>();
        var query = context
            .Set<DateTimeEntity>()
            .Where(e => e.BirthDate.AddDays(7) > new DateOnly(2025, 1, 1))
            .ToQueryString();

        MySqlSqlAssert.ContainsDateAdd(query, "DAY");
    }

    /// <summary>
    /// DateOnly.AddMonths translates to DATE_ADD with INTERVAL MONTH.
    /// </summary>
    [Fact]
    public void DateOnly_AddMonths_translates_to_date_add()
    {
        using var context = CreateContext<DateTimeContext>();
        var query = context
            .Set<DateTimeEntity>()
            .Where(e => e.BirthDate.AddMonths(3) > new DateOnly(2025, 1, 1))
            .ToQueryString();

        MySqlSqlAssert.ContainsDateAdd(query, "MONTH");
    }

    /// <summary>
    /// DateOnly.AddYears translates to DATE_ADD with INTERVAL YEAR.
    /// </summary>
    [Fact]
    public void DateOnly_AddYears_translates_to_date_add()
    {
        using var context = CreateContext<DateTimeContext>();
        var query = context
            .Set<DateTimeEntity>()
            .Where(e => e.BirthDate.AddYears(1) > new DateOnly(2025, 1, 1))
            .ToQueryString();

        MySqlSqlAssert.ContainsDateAdd(query, "YEAR");
    }

    /// <summary>
    /// TimeOnly.Add(TimeSpan) translates to ADDTIME.
    /// </summary>
    [Fact]
    public void TimeOnly_Add_translates_to_addtime()
    {
        using var context = CreateContext<DateTimeContext>();
        var query = context
            .Set<DateTimeEntity>()
            .Where(e => e.StartTime.Add(TimeSpan.FromHours(1)) > new TimeOnly(12, 0))
            .ToQueryString();

        MySqlSqlAssert.ContainsFunction(query, "ADDTIME");
    }

    // -- MySQL-family INVISIBLE Columns --

    /// <summary>
    /// IsInvisible annotation generates INVISIBLE keyword in column DDL.
    /// </summary>
    [Fact]
    public void IsInvisible_generates_invisible_ddl()
    {
        using var source = CreateContext<EmptyInvisibleContext>(isMariaDb: true);
        using var context = CreateContext<InvisibleContext>(isMariaDb: true);
        var operations = GetDifferences(source, context);
        var createTable = Assert.Single(operations.OfType<CreateTableOperation>());
        var invisibleColumn = Assert.Single(
            createTable.Columns,
            column => column.Name == nameof(InvisibleEntity.InternalData));

        Assert.Equal(true, invisibleColumn.FindAnnotation(MySqlAnnotationNames.Invisible)?.Value);

        var generator = context.GetService<IMigrationsSqlGenerator>();
        var commands = generator.Generate(operations, context.Model);
        var sql = string.Join("\n", commands.Select(c => c.CommandText));

        Assert.Contains("INVISIBLE", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Adding an invisible property to an existing table carries the model annotation
    /// into the standalone column operation.
    /// </summary>
    [Fact]
    public void IsInvisible_propagates_to_standalone_add_column()
    {
        using var source = CreateContext<InvisibleWithoutColumnContext>(isMariaDb: true);
        using var target = CreateContext<InvisibleContext>(isMariaDb: true);

        var operation = Assert.Single(GetDifferences(source, target).OfType<AddColumnOperation>());

        Assert.Equal(nameof(InvisibleEntity.InternalData), operation.Name);
        Assert.Equal(true, operation.FindAnnotation(MySqlAnnotationNames.Invisible)?.Value);
    }

    /// <summary>
    /// Visibility changes preserve both sides of the migration so generated up and
    /// down operations deterministically add or remove the INVISIBLE attribute.
    /// </summary>
    [Fact]
    public void IsInvisible_propagates_through_bidirectional_alter_column()
    {
        using var invisible = CreateContext<InvisibleContext>(isMariaDb: true);
        using var visible = CreateContext<VisibleColumnContext>(isMariaDb: true);

        var makeVisible = Assert.Single(GetDifferences(invisible, visible).OfType<AlterColumnOperation>());
        var makeInvisible = Assert.Single(GetDifferences(visible, invisible).OfType<AlterColumnOperation>());

        Assert.Equal(false, makeVisible.FindAnnotation(MySqlAnnotationNames.Invisible)?.Value);
        Assert.Equal(true, makeVisible.OldColumn.FindAnnotation(MySqlAnnotationNames.Invisible)?.Value);
        Assert.Equal(true, makeInvisible.FindAnnotation(MySqlAnnotationNames.Invisible)?.Value);
        Assert.Equal(false, makeInvisible.OldColumn.FindAnnotation(MySqlAnnotationNames.Invisible)?.Value);

        var visibleSql = string.Join(
            "\n",
            visible
                .GetService<IMigrationsSqlGenerator>()
                .Generate([makeVisible], visible.Model)
                .Select(command => command.CommandText));

        var invisibleSql = string.Join(
            "\n",
            invisible
                .GetService<IMigrationsSqlGenerator>()
                .Generate([makeInvisible], invisible.Model)
                .Select(command => command.CommandText));

        Assert.DoesNotContain("INVISIBLE", visibleSql, StringComparison.Ordinal);
        Assert.Contains("INVISIBLE", invisibleSql, StringComparison.Ordinal);
    }

    /// <summary>
    /// IsInvisible fluent API sets the annotation correctly.
    /// </summary>
    [Fact]
    public void IsInvisible_fluent_api_sets_annotation()
    {
        using var context = CreateContext<InvisibleContext>(isMariaDb: true);
        var property =
            context.Model.FindEntityType(typeof(InvisibleEntity))!.FindProperty(nameof(InvisibleEntity.InternalData))!;

        var annotation = property.FindAnnotation(MySqlAnnotationNames.Invisible);
        Assert.NotNull(annotation);
        Assert.Equal(true, annotation.Value);
    }

    // -- JSON Manipulation Functions --

    /// <summary>
    /// EF.Functions.JsonSet translates to JSON_SET SQL.
    /// </summary>
    [Fact]
    public void JsonSet_translates_to_json_set_sql()
    {
        using var context = CreateContext<JsonFunctionContext>();
        var query = context
            .Set<JsonFunctionEntity>()
            .Select(e => EF.Functions.JsonSet(e.Data, "$.name", "test"))
            .ToQueryString();

        MySqlSqlAssert.ContainsFunction(query, "JSON_SET");
    }

    /// <summary>
    /// EF.Functions.JsonRemove translates to JSON_REMOVE SQL.
    /// </summary>
    [Fact]
    public void JsonRemove_translates_to_json_remove_sql()
    {
        using var context = CreateContext<JsonFunctionContext>();
        var query = context
            .Set<JsonFunctionEntity>()
            .Select(e => EF.Functions.JsonRemove(e.Data, "$.name"))
            .ToQueryString();

        MySqlSqlAssert.ContainsFunction(query, "JSON_REMOVE");
    }

    /// <summary>
    /// Ordinary C# params syntax is flattened into independent JSON_ARRAY SQL
    /// arguments while captured values remain parameters.
    /// </summary>
    [Fact]
    public void JsonArray_params_translate_to_variadic_parameterized_sql()
    {
        using var context = CreateContext<JsonFunctionContext>();
        var suffix = "tail";
        var query = context
            .Set<JsonFunctionEntity>()
            .Select(entity => EF.Functions.JsonArray(entity.Data, suffix))
            .ToQueryString();

        Assert.Contains("JSON_ARRAY(`j`.`Data", query, StringComparison.Ordinal);
        Assert.Contains("@suffix", query, StringComparison.Ordinal);
    }

    /// <summary>
    /// JSON_OBJECT receives alternating keys and values as individual SQL arguments.
    /// </summary>
    [Fact]
    public void JsonObject_params_translate_to_variadic_sql()
    {
        using var context = CreateContext<JsonFunctionContext>();
        var query = context
            .Set<JsonFunctionEntity>()
            .Select(entity => EF.Functions.JsonObject("data", entity.Data, "id", entity.Id))
            .ToQueryString();

        Assert.Contains("JSON_OBJECT('data', `j`.`Data`, 'id', `j`.`Id`)", query, StringComparison.Ordinal);
    }

    /// <summary>
    /// SQL NULL remains an argument of each JSON constructor instead of making the
    /// constructor untranslatable or null-propagating the complete result.
    /// </summary>
    [Fact]
    public void Json_constructors_translate_null_arguments_as_json_null_inputs()
    {
        using var context = CreateContext<JsonFunctionContext>();

        var arraySql = context
            .Set<JsonFunctionEntity>()
            .Select(entity => EF.Functions.JsonArray(entity.Data, null))
            .ToQueryString();

        var objectSql = context
            .Set<JsonFunctionEntity>()
            .Select(entity => EF.Functions.JsonObject("data", entity.Data, "missing", null))
            .ToQueryString();

        Assert.Contains("JSON_ARRAY(`j`.`Data`, NULL)", arraySql, StringComparison.Ordinal);
        Assert.Contains(
            "JSON_OBJECT('data', `j`.`Data`, 'missing', NULL)",
            objectSql,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Empty params arrays map to the engines' documented empty JSON constructors.
    /// </summary>
    [Fact]
    public void Empty_json_constructors_translate_without_arguments()
    {
        using var context = CreateContext<JsonFunctionContext>();

        var arraySql = context
            .Set<JsonFunctionEntity>()
            .Select(_ => EF.Functions.JsonArray())
            .ToQueryString();

        var objectSql = context
            .Set<JsonFunctionEntity>()
            .Select(_ => EF.Functions.JsonObject())
            .ToQueryString();

        Assert.Contains("JSON_ARRAY()", arraySql, StringComparison.Ordinal);
        Assert.Contains("JSON_OBJECT()", objectSql, StringComparison.Ordinal);
    }

    /// <summary>
    /// An incomplete key/value pair is rejected during translation with a focused
    /// provider diagnostic instead of reaching query execution.
    /// </summary>
    [Fact]
    public void JsonObject_rejects_an_odd_argument_count_before_execution()
    {
        using var context = CreateContext<JsonFunctionContext>();

        var exception = Assert.Throws<InvalidOperationException>(() => context
            .Set<JsonFunctionEntity>()
            .Select(entity => EF.Functions.JsonObject("data", entity.Data, "orphan"))
            .ToQueryString());

        Assert.Contains("even number of key and value arguments", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Composite client objects cannot cross the JSON-construction translation
    /// boundary because their representation is not a database scalar.
    /// </summary>
    [Fact]
    public void JsonArray_rejects_an_unsupported_nested_client_value_before_execution()
    {
        using var context = CreateContext<JsonFunctionContext>();

        var exception = Assert.Throws<InvalidOperationException>(() => context
            .Set<JsonFunctionEntity>()
            .Select(entity => EF.Functions.JsonArray(new UnsupportedJsonValue(entity.Data)))
            .ToQueryString());

        Assert.Contains("argument 1 cannot be translated to SQL", exception.Message, StringComparison.Ordinal);
    }

    // -- JSON Inspection Functions --

    /// <summary>
    /// EF.Functions.JsonDepth translates to JSON_DEPTH SQL.
    /// </summary>
    [Fact]
    public void JsonDepth_translates_to_json_depth_sql()
    {
        using var context = CreateContext<JsonFunctionContext>();
        var query = context
            .Set<JsonFunctionEntity>()
            .Select(e => EF.Functions.JsonDepth(e.Data))
            .ToQueryString();

        MySqlSqlAssert.ContainsFunction(query, "JSON_DEPTH");
    }

    /// <summary>
    /// EF.Functions.JsonLength translates to JSON_LENGTH SQL.
    /// </summary>
    [Fact]
    public void JsonLength_translates_to_json_length_sql()
    {
        using var context = CreateContext<JsonFunctionContext>();
        var query = context
            .Set<JsonFunctionEntity>()
            .Select(e => EF.Functions.JsonLength(e.Data))
            .ToQueryString();

        MySqlSqlAssert.ContainsFunction(query, "JSON_LENGTH");
    }

    /// <summary>
    /// EF.Functions.JsonType translates to JSON_TYPE SQL.
    /// </summary>
    [Fact]
    public void JsonType_translates_to_json_type_sql()
    {
        using var context = CreateContext<JsonFunctionContext>();
        var query = context
            .Set<JsonFunctionEntity>()
            .Select(e => EF.Functions.JsonType(e.Data))
            .ToQueryString();

        MySqlSqlAssert.ContainsFunction(query, "JSON_TYPE");
    }

    /// <summary>
    /// EF.Functions.JsonKeys translates to JSON_KEYS SQL.
    /// </summary>
    [Fact]
    public void JsonKeys_translates_to_json_keys_sql()
    {
        using var context = CreateContext<JsonFunctionContext>();
        var query = context
            .Set<JsonFunctionEntity>()
            .Select(e => EF.Functions.JsonKeys(e.Data))
            .ToQueryString();

        MySqlSqlAssert.ContainsFunction(query, "JSON_KEYS");
    }

    /// <summary>
    /// EF.Functions.JsonContains translates to JSON_CONTAINS SQL.
    /// </summary>
    [Fact]
    public void JsonContains_translates_to_json_contains_sql()
    {
        using var context = CreateContext<JsonFunctionContext>();
        var query = context
            .Set<JsonFunctionEntity>()
            .Where(e => EF.Functions.JsonContains(e.Data, "\"value\""))
            .ToQueryString();

        MySqlSqlAssert.ContainsFunction(query, "JSON_CONTAINS");
    }

    // -- Helpers --

    private static TContext CreateContext<TContext>(
        bool isMariaDb = false
    )
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();
        var serverVersion = isMariaDb
            ? MySqlServerVersion.MariaDb(new Version(11, 8, 0))
            : MySqlServerVersion.MySql(new Version(8, 4, 0));

        builder.UseMySql("Server=localhost;Database=doka;User ID=root;Password=password;", serverVersion);

        return (TContext)Activator.CreateInstance(typeof(TContext), builder.Options)!;
    }

    private static IReadOnlyList<MigrationOperation> GetDifferences(
        DbContext source,
        DbContext target
    ) => target
        .GetService<IMigrationsModelDiffer>()
        .GetDifferences(
            source.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
            target.GetService<IDesignTimeModel>().Model.GetRelationalModel());

    // -- Entities and Contexts --

    private sealed class HiLoEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class HiLoContext : DbContext
    {
        public HiLoContext(
            DbContextOptions<HiLoContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<HiLoEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.Id)
                    .UseHiLo("HiLoTestSequence");
            });
        }
    }

    private sealed class HiLoDefaultEntity
    {
        public int Id { get; set; }
    }

    private sealed class HiLoDefaultContext : DbContext
    {
        public HiLoDefaultContext(
            DbContextOptions<HiLoDefaultContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<HiLoDefaultEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.Id)
                    .UseHiLo();
            });
        }
    }

    private sealed class DateTimeEntity
    {
        public int Id { get; set; }
        public DateOnly BirthDate { get; set; }
        public TimeOnly StartTime { get; set; }
    }

    private sealed class DateTimeContext : DbContext
    {
        public DateTimeContext(
            DbContextOptions<DateTimeContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<DateTimeEntity>(e => { e.HasKey(x => x.Id); });
    }

    private sealed class InvisibleEntity
    {
        public int Id { get; set; }
        public string InternalData { get; set; } = "";
    }

    private sealed class InvisibleContext : DbContext
    {
        public InvisibleContext(
            DbContextOptions<InvisibleContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<InvisibleEntity>(e =>
            {
                e.ToTable("InvisibleEntities");
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.InternalData)
                    .HasDefaultValue(string.Empty)
                    .IsInvisible();
            });
        }
    }

    private sealed class EmptyInvisibleContext : DbContext
    {
        public EmptyInvisibleContext(
            DbContextOptions<EmptyInvisibleContext> options
        ) : base(options) { }
    }

    private sealed class InvisibleWithoutColumnContext : DbContext
    {
        public InvisibleWithoutColumnContext(
            DbContextOptions<InvisibleWithoutColumnContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<InvisibleEntity>(entity =>
            {
                entity.ToTable("InvisibleEntities");
                entity.HasKey(value => value.Id);
                entity.Ignore(value => value.InternalData);
            });
        }
    }

    private sealed class VisibleColumnContext : DbContext
    {
        public VisibleColumnContext(
            DbContextOptions<VisibleColumnContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<InvisibleEntity>(entity =>
            {
                entity.ToTable("InvisibleEntities");
                entity.HasKey(value => value.Id);
                entity
                    .Property(value => value.InternalData)
                    .HasDefaultValue(string.Empty)
                    .IsInvisible(false);
            });
        }
    }

    private sealed class JsonFunctionEntity
    {
        public int Id { get; set; }
        public string Data { get; set; } = "{}";
    }

    private sealed record UnsupportedJsonValue(string Value);

    private sealed class JsonFunctionContext : DbContext
    {
        public JsonFunctionContext(
            DbContextOptions<JsonFunctionContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<JsonFunctionEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.Data)
                    .HasColumnType("json");
            });
        }
    }
}
