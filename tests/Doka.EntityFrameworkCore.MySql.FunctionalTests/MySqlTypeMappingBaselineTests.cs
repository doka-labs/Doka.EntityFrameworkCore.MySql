namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies the DDL/type-mapping and schema-validation baseline.
/// </summary>
public sealed class MySqlTypeMappingBaselineTests
{
    /// <summary>
    /// Verifies that the documented text, binary, enum, decimal, and temporal defaults are reflected in the model.
    /// </summary>
    [Fact]
    public void Model_store_types_follow_the_package5_contract()
    {
        using var context = new TypeMappingContext(CreateOptions<TypeMappingContext>());
        var entityType = context.Model.FindEntityType(typeof(TypeMappingEntity))!;

        Assert.Equal("varchar(32)", entityType.FindProperty(nameof(TypeMappingEntity.BoundedText))!.GetColumnType());
        Assert.Equal("char(8)", entityType.FindProperty(nameof(TypeMappingEntity.FixedText))!.GetColumnType());
        Assert.Equal("longtext", entityType.FindProperty(nameof(TypeMappingEntity.UnboundedText))!.GetColumnType());
        Assert.Equal(
            "varbinary(64)",
            entityType.FindProperty(nameof(TypeMappingEntity.BoundedBinary))!.GetColumnType());
        Assert.Equal("longblob", entityType.FindProperty(nameof(TypeMappingEntity.UnboundedBinary))!.GetColumnType());
        Assert.Equal("smallint", entityType.FindProperty(nameof(TypeMappingEntity.Status))!.GetColumnType());
        Assert.Equal(
            "varchar(24)",
            entityType.FindProperty(nameof(TypeMappingEntity.StringBackedStatus))!.GetColumnType());
        Assert.Equal("decimal(18,2)", entityType.FindProperty(nameof(TypeMappingEntity.Amount))!.GetColumnType());
        Assert.Equal(
            "decimal(18,2)",
            entityType.FindProperty(nameof(TypeMappingEntity.ExplicitAmount))!.GetColumnType());
        Assert.Equal("datetime(6)", entityType.FindProperty(nameof(TypeMappingEntity.CreatedAt))!.GetColumnType());
        Assert.Equal("time(6)", entityType.FindProperty(nameof(TypeMappingEntity.OpenAt))!.GetColumnType());
        Assert.Equal("timestamp(3)", entityType.FindProperty(nameof(TypeMappingEntity.ObservedAt))!.GetColumnType());
    }

    /// <summary>
    /// Verifies that ANSI/unicode hints do not move the provider out of the utf8mb4 text-store family.
    /// </summary>
    [Fact]
    public void Is_unicode_false_does_not_change_the_text_store_family()
    {
        using var context = new TypeMappingContext(CreateOptions<TypeMappingContext>());
        var property =
            context.Model.FindEntityType(typeof(TypeMappingEntity))!.FindProperty(
                nameof(TypeMappingEntity.AnsiHintText))!;

        Assert.Equal("varchar(20)", property.GetColumnType());
        Assert.False(property.IsUnicode() ?? true);
    }

    /// <summary>
    /// Verifies that explicit tinyint store types preserve the signed-byte versus boolean distinction.
    /// </summary>
    [Fact]
    public void Tinyint_store_types_distinguish_sbyte_from_bool()
    {
        using var context = new TypeMappingContext(CreateOptions<TypeMappingContext>());
        var typeMappingSource = context.GetService<IRelationalTypeMappingSource>();

        var tinyIntMapping = typeMappingSource.FindMapping("tinyint");
        var tinyIntBooleanMapping = typeMappingSource.FindMapping("tinyint(1)");

        Assert.NotNull(tinyIntMapping);
        Assert.NotNull(tinyIntBooleanMapping);
        Assert.Equal(typeof(sbyte), tinyIntMapping.ClrType);
        Assert.Equal("tinyint", tinyIntMapping.StoreType);
        Assert.Equal(typeof(bool), tinyIntBooleanMapping.ClrType);
        Assert.Equal("tinyint(1)", tinyIntBooleanMapping.StoreType);
    }

    /// <summary>
    /// Verifies that explicit model/property collation metadata remains intact through model building.
    /// </summary>
    [Fact]
    public void Explicit_collation_metadata_is_preserved_on_the_model()
    {
        using var context = new TypeMappingContext(CreateOptions<TypeMappingContext>());
        var designTimeModel = context.GetService<IDesignTimeModel>()
            .Model;
        var entityType = designTimeModel.FindEntityType(typeof(TypeMappingEntity))!;
        var property = entityType.FindProperty(nameof(TypeMappingEntity.CollatedText))!;

        Assert.Equal("utf8mb4_bin", designTimeModel.GetCollation());
        Assert.Equal("utf8mb4_0900_as_cs", property.GetCollation());
    }

    /// <summary>
    /// Verifies that generated migration SQL uses the documented enum and decimal store types.
    /// </summary>
    [Fact]
    public void Migrations_sql_generator_uses_numeric_enum_and_explicit_decimal_store_types()
    {
        using var context = new TypeMappingContext(CreateOptions<TypeMappingContext>());
        var migrationsSqlGenerator = context.GetService<IMigrationsSqlGenerator>();
        var commands = migrationsSqlGenerator.Generate(
            new MigrationOperation[]
            {
                new CreateTableOperation
                {
                    Name = "Phase2TypeMappingProbe",
                    Columns =
                    {
                        new AddColumnOperation
                        {
                            Name = "Status",
                            ClrType = typeof(StatusCode),
                            IsNullable = false,
                        },
                        new AddColumnOperation
                        {
                            Name = "Amount",
                            ClrType = typeof(decimal),
                            Precision = 18,
                            Scale = 2,
                            IsNullable = false,
                        },
                    },
                },
            },
            context.Model);

        var command = Assert.Single(commands);

        Assert.Contains("`Status` smallint NOT NULL", command.CommandText, StringComparison.Ordinal);
        Assert.Contains("`Amount` decimal(18,2) NOT NULL", command.CommandText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that keyed strings without an explicit length receive the
    /// provider's index-safe bounded default.
    /// </summary>
    [Fact]
    public void Keyed_text_without_explicit_max_length_uses_bounded_default()
    {
        using var context = new ImplicitKeyedTextContext(
            CreateOptions<ImplicitKeyedTextContext>());
        var property = context.Model
            .FindEntityType(typeof(ImplicitKeyedTextEntity))!
            .FindProperty(nameof(ImplicitKeyedTextEntity.Code))!;

        Assert.Equal("varchar(255)", property.GetColumnType());
    }

    /// <summary>
    /// Verifies that indexed binary properties without an explicit length
    /// receive the same index-safe bounded default.
    /// </summary>
    [Fact]
    public void Indexed_binary_without_explicit_max_length_uses_bounded_default()
    {
        using var context = new ImplicitIndexedBinaryContext(
            CreateOptions<ImplicitIndexedBinaryContext>());
        var property = context.Model
            .FindEntityType(typeof(ImplicitIndexedBinaryEntity))!
            .FindProperty(nameof(ImplicitIndexedBinaryEntity.Token))!;

        Assert.Equal("varbinary(255)", property.GetColumnType());
    }

    private static DbContextOptions<TContext> CreateOptions<TContext>()
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();

        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        return builder.Options;
    }

    private sealed class TypeMappingContext : DbContext
    {
        public TypeMappingContext(
            DbContextOptions<TypeMappingContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.UseCollation("utf8mb4_bin");

            modelBuilder.Entity<TypeMappingEntity>(entity =>
            {
                entity.ToTable("Phase2TypeMappingEntities");
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.BoundedText)
                    .HasMaxLength(32);
                entity
                    .Property(item => item.FixedText)
                    .IsFixedLength()
                    .HasMaxLength(8);
                entity
                    .Property(item => item.BoundedBinary)
                    .HasMaxLength(64);
                entity
                    .Property(item => item.StringBackedStatus)
                    .HasConversion<string>()
                    .HasMaxLength(24);
                entity
                    .Property(item => item.ExplicitAmount)
                    .HasPrecision(18, 2);
                entity
                    .Property(item => item.AnsiHintText)
                    .HasMaxLength(20)
                    .IsUnicode(false);
                entity
                    .Property(item => item.CollatedText)
                    .HasMaxLength(40)
                    .UseCollation("utf8mb4_0900_as_cs");
                entity
                    .Property(item => item.ObservedAt)
                    .HasColumnType("timestamp(3)");
            });
        }
    }

    private sealed class ImplicitKeyedTextContext : DbContext
    {
        public ImplicitKeyedTextContext(
            DbContextOptions<ImplicitKeyedTextContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<ImplicitKeyedTextEntity>(entity =>
            {
                entity.ToTable("Phase2ImplicitKeyedTextEntities");
                entity.HasKey(item => item.Code);
            });
        }
    }

    private sealed class ImplicitIndexedBinaryContext : DbContext
    {
        public ImplicitIndexedBinaryContext(
            DbContextOptions<ImplicitIndexedBinaryContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<ImplicitIndexedBinaryEntity>(entity =>
            {
                entity.ToTable("Phase2ImplicitIndexedBinaryEntities");
                entity.HasKey(item => item.Id);
                entity.HasIndex(item => item.Token);
            });
        }
    }

    private sealed class TypeMappingEntity
    {
        public int Id { get; set; }

        public string BoundedText { get; set; } = string.Empty;

        public string FixedText { get; set; } = string.Empty;

        public string UnboundedText { get; set; } = string.Empty;

        public byte[] BoundedBinary { get; set; } = Array.Empty<byte>();

        public byte[] UnboundedBinary { get; set; } = Array.Empty<byte>();

        public StatusCode Status { get; set; }

        public StatusCode StringBackedStatus { get; set; }

        public decimal Amount { get; set; }

        public decimal ExplicitAmount { get; set; }

        public DateTime CreatedAt { get; set; }

        public TimeOnly OpenAt { get; set; }

        public DateTime ObservedAt { get; set; }

        public string AnsiHintText { get; set; } = string.Empty;

        public string CollatedText { get; set; } = string.Empty;
    }

    private sealed class ImplicitKeyedTextEntity
    {
        public string Code { get; set; } = string.Empty;
    }

    private sealed class ImplicitIndexedBinaryEntity
    {
        public int Id { get; set; }

        public byte[] Token { get; set; } = Array.Empty<byte>();
    }

    private enum StatusCode : short
    {
        Unknown = 0,
        Ready = 1,
    }

    /// <summary>
    /// Verifies that BoolToStringConverter produces a varchar column, not tinyint(1).
    /// Validates ValueConverter provider type propagation.
    /// </summary>
    [Fact]
    public void Bool_to_string_converter_produces_varchar_column()
    {
        using var context = new ValueConverterContext(CreateOptions<ValueConverterContext>());
        var entityType = context.Model.FindEntityType(typeof(ValueConverterEntity))!;

        Assert.Equal("varchar(1)", entityType.FindProperty(nameof(ValueConverterEntity.IsActiveText))!.GetColumnType());
    }

    /// <summary>
    /// Verifies that a custom domain type converter (Money -> decimal) produces decimal column.
    /// </summary>
    [Fact]
    public void Custom_domain_type_converter_produces_correct_column_type()
    {
        using var context = new ValueConverterContext(CreateOptions<ValueConverterContext>());
        var entityType = context.Model.FindEntityType(typeof(ValueConverterEntity))!;

        Assert.Equal("decimal(18,2)", entityType.FindProperty(nameof(ValueConverterEntity.Price))!.GetColumnType());
    }

    /// <summary>
    /// Verifies that TimeSpanToTicksConverter produces bigint column.
    /// </summary>
    [Fact]
    public void Timespan_to_ticks_converter_produces_bigint_column()
    {
        using var context = new ValueConverterContext(CreateOptions<ValueConverterContext>());
        var entityType = context.Model.FindEntityType(typeof(ValueConverterEntity))!;

        Assert.Equal("bigint", entityType.FindProperty(nameof(ValueConverterEntity.DurationTicks))!.GetColumnType());
    }

    /// <summary>
    /// Verifies that Uri property with UriToStringConverter produces a text column.
    /// </summary>
    [Fact]
    public void Uri_to_string_converter_produces_text_column()
    {
        using var context = new ValueConverterContext(CreateOptions<ValueConverterContext>());
        var entityType = context.Model.FindEntityType(typeof(ValueConverterEntity))!;
        var columnType = entityType.FindProperty(nameof(ValueConverterEntity.Website))!.GetColumnType();

        Assert.True(
            columnType == "varchar(2048)" || columnType == "longtext",
            $"Expected varchar(2048) or longtext but got '{columnType}'.");
    }

    /// <summary>
    /// BoolToZeroOneConverter produces int column.
    /// </summary>
    [Fact]
    public void Bool_to_zero_one_converter_produces_int_column()
    {
        using var context = new ValueConverterContext(CreateOptions<ValueConverterContext>());
        var entityType = context.Model.FindEntityType(typeof(ValueConverterEntity))!;
        var columnType = entityType.FindProperty(nameof(ValueConverterEntity.IsVerifiedInt))!.GetColumnType();

        Assert.Equal("int", columnType);
    }

    /// <summary>
    /// DateTimeOffset to DateTime custom converter produces datetime(6) column.
    /// </summary>
    [Fact]
    public void Datetimeoffset_to_datetime_converter_produces_datetime_column()
    {
        using var context = new ValueConverterContext(CreateOptions<ValueConverterContext>());
        var entityType = context.Model.FindEntityType(typeof(ValueConverterEntity))!;
        var columnType = entityType.FindProperty(nameof(ValueConverterEntity.LastModified))!.GetColumnType();

        Assert.Equal("datetime(6)", columnType);
    }

    /// <summary>
    /// Converter in OrderBy produces correct SQL.
    /// </summary>
    [Fact]
    public void Converter_in_orderby_produces_valid_sql()
    {
        using var context = new ValueConverterContext(CreateOptions<ValueConverterContext>());
        var sql = context
            .Set<ValueConverterEntity>()
            .OrderBy(e => e.Price)
            .ToQueryString();

        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ValueConverterContext : DbContext
    {
        public ValueConverterContext(
            DbContextOptions<ValueConverterContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<ValueConverterEntity>(entity =>
            {
                entity.ToTable("ValueConverterEntities");
                entity.HasKey(item => item.Id);

                entity
                    .Property(item => item.IsActiveText)
                    .HasConversion(v => v ? "Y" : "N", v => v == "Y")
                    .HasMaxLength(1);

                entity
                    .Property(item => item.Price)
                    .HasConversion(v => v.Amount, v => new Money(v))
                    .HasPrecision(18, 2);

                entity
                    .Property(item => item.DurationTicks)
                    .HasConversion<long>();

                entity
                    .Property(item => item.Website)
                    .HasConversion(v => v == null ? null : v.AbsoluteUri, v => v == null ? null : new Uri(v))
                    .HasMaxLength(2048);

                entity
                    .Property(item => item.IsVerifiedInt)
                    .HasConversion<int>();

                entity
                    .Property(item => item.LastModified)
                    .HasConversion(v => v.UtcDateTime, v => new DateTimeOffset(v, TimeSpan.Zero));
            });
        }
    }

    private sealed class ValueConverterEntity
    {
        public int Id { get; set; }

        public bool IsActiveText { get; set; }

        public bool IsVerifiedInt { get; set; }

        public Money Price { get; set; }

        public TimeSpan DurationTicks { get; set; }

        public Uri? Website { get; set; }

        public DateTimeOffset LastModified { get; set; }
    }

    private readonly record struct Money(decimal Amount);
}
