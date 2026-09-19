namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies provider-specific translation of parameterized primitive collections.
/// </summary>
public sealed class MySqlPrimitiveCollectionParameterTests
{
    private static readonly Guid s_first = Guid.Parse("00000001-0001-0002-0304-05060708090a");
    private static readonly Guid s_second = Guid.Parse("00000002-0002-0003-0405-060708090a0b");

    /// <summary>
    /// A deferred Binary16 element mapping must decode the JSON GUID text before comparison.
    /// </summary>
    [Fact]
    public void Binary_guid_parameter_collection_decodes_json_text_after_type_inference()
    {
        using var context = CreateContext();
        var values = new[] { s_first, s_second };

        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Contains(item.BinaryId))
            .ToQueryString();

        Assert.Contains("`value` char(36) PATH '$'", sql, StringComparison.Ordinal);
        Assert.Contains("UNHEX(REPLACE(`v`.`value`, '-', ''))", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` binary(16) PATH '$'", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// A deferred Char36 element mapping must retain textual extraction without binary decoding.
    /// </summary>
    [Fact]
    public void Char_guid_parameter_collection_preserves_text_after_type_inference()
    {
        using var context = CreateContext();
        var values = new[] { s_first, s_second };

        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Contains(item.CharId))
            .ToQueryString();

        Assert.Contains("`value` char(36) PATH '$'", sql, StringComparison.Ordinal);
        Assert.Contains("JSON_UNQUOTE(JSON_QUOTE(`v`.`value`))", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("UNHEX(", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` binary(16) PATH '$'", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nullable Binary16 values use the same deferred decoding path without losing null semantics.
    /// </summary>
    [Fact]
    public void Nullable_binary_guid_parameter_collection_decodes_json_text_after_type_inference()
    {
        using var context = CreateContext();
        Guid?[] values = [s_first, null];

        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Contains(item.OptionalBinaryId))
            .ToQueryString();

        Assert.Contains("`value` char(36) PATH '$'", sql, StringComparison.Ordinal);
        Assert.Contains("UNHEX(REPLACE(`v`.`value`, '-', ''))", sql, StringComparison.Ordinal);
        Assert.Contains("IS NULL", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nullable Char36 values retain text decoding and null semantics after
    /// deferred type inference.
    /// </summary>
    [Fact]
    public void Nullable_char_guid_parameter_collection_preserves_text_after_type_inference()
    {
        using var context = CreateContext();
        Guid?[] values = [s_first, null];

        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Contains(item.OptionalCharId))
            .ToQueryString();

        Assert.Contains("`value` char(36) PATH '$'", sql, StringComparison.Ordinal);
        Assert.Contains("JSON_UNQUOTE(JSON_QUOTE(`v`.`value`))", sql, StringComparison.Ordinal);
        Assert.Contains("IS NULL", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("UNHEX(", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// A deferred string mapping must remain visible to EF Core's inference
    /// pass and preserve arbitrary SQL text after extraction.
    /// </summary>
    [Fact]
    public void String_parameter_collection_decodes_text_after_type_inference()
    {
        using var context = CreateContext();
        string[] values = ["alpha", "\"quoted\"", "\"\\q\""];

        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Contains(item.Name))
            .ToQueryString();

        Assert.Contains("`value` longtext PATH '$'", sql, StringComparison.Ordinal);
        Assert.Contains(
            "JSON_UNQUOTE(JSON_QUOTE(CONVERT(FROM_BASE64(`v`.`value`) USING utf8mb4)))",
            sql,
            StringComparison.Ordinal);
        Assert.Contains("YWxwaGE=", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` varchar(64) PATH '$'", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nullable string parameters retain null membership while non-null values
    /// use the SQL-mode-independent transport.
    /// </summary>
    [Fact]
    public void Nullable_string_parameter_collection_preserves_null_semantics()
    {
        using var context = CreateContext();
        string?[] values = ["alpha", null];

        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Contains(item.OptionalName))
            .ToQueryString();

        Assert.Contains(
            "JSON_UNQUOTE(JSON_QUOTE(CONVERT(FROM_BASE64(`v`.`value`) USING utf8mb4)))",
            sql,
            StringComparison.Ordinal);
        Assert.Contains("IS NULL", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// String-to-string converters run before Base64 transport without
    /// reintroducing the property's length facet into JSON extraction.
    /// </summary>
    [Fact]
    public void Converted_string_parameter_collection_uses_provider_representation()
    {
        using var context = CreateContext();
        string[] values = ["alpha"];

        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Contains(item.ConvertedName))
            .ToQueryString();

        Assert.Contains("`value` longtext PATH '$'", sql, StringComparison.Ordinal);
        Assert.Contains(
            "JSON_UNQUOTE(JSON_QUOTE(CONVERT(FROM_BASE64(`v`.`value`) USING utf8mb4)))",
            sql,
            StringComparison.Ordinal);
        Assert.Contains("ZGItYWxwaGE=", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` varchar(64) PATH '$'", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// A model-to-string converter exposes its provider representation to
    /// JSON extraction without inheriting the property's length facet.
    /// </summary>
    [Fact]
    public void Enum_to_string_parameter_collection_uses_provider_representation()
    {
        using var context = CreateContext();
        CollectionStatus[] values = [CollectionStatus.Active];

        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Contains(item.Status))
            .ToQueryString();

        Assert.Contains("`value` longtext PATH '$'", sql, StringComparison.Ordinal);
        Assert.Contains("QWN0aXZl", sql, StringComparison.Ordinal);
        Assert.Contains("FROM_BASE64(`v`.`value`)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` varchar(16) PATH '$'", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nullable model-to-string values retain converter and null-membership
    /// semantics through deferred collection mapping.
    /// </summary>
    [Fact]
    public void Nullable_enum_to_string_parameter_collection_preserves_null_semantics()
    {
        using var context = CreateContext();
        CollectionStatus?[] values = [CollectionStatus.Active, null];

        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Contains(item.OptionalStatus))
            .ToQueryString();

        Assert.Contains("`value` longtext PATH '$'", sql, StringComparison.Ordinal);
        Assert.Contains("FROM_BASE64(`v`.`value`)", sql, StringComparison.Ordinal);
        Assert.Contains("IS NULL", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` varchar(16) PATH '$'", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// A value object with a string provider uses the same SQL-mode-independent
    /// transport as a native string.
    /// </summary>
    [Fact]
    public void Value_object_to_string_parameter_collection_uses_base64_transport()
    {
        using var context = CreateContext();
        TextValue[] values = [new("\"\\q\"")];

        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Contains(item.ConvertedText))
            .ToQueryString();

        Assert.Contains("`value` longtext PATH '$'", sql, StringComparison.Ordinal);
        Assert.Contains(
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(values[0].Value)),
            sql,
            StringComparison.Ordinal);
        Assert.Contains("FROM_BASE64(`v`.`value`)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` varchar(64) PATH '$'", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// A model-to-decimal converter uses a lossless provider mapping before
    /// the configured property precision participates in comparison.
    /// </summary>
    [Fact]
    public void Value_object_to_decimal_parameter_collection_uses_provider_representation()
    {
        using var context = CreateContext();
        PriceValue[] values = [new(3.14159m)];

        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Contains(item.ConvertedPrice))
            .ToQueryString();

        Assert.Contains("`value` decimal(65,30) PATH '$'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` decimal(10,1) PATH '$'", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// A model-to-Guid converter retains Binary16 decoding at the JSON
    /// transport boundary.
    /// </summary>
    [Fact]
    public void Value_object_to_guid_parameter_collection_uses_provider_representation()
    {
        using var context = CreateContext();
        StrongGuid[] values = [new(s_first)];

        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Contains(item.ConvertedGuid))
            .ToQueryString();

        Assert.Contains("`value` char(36) PATH '$'", sql, StringComparison.Ordinal);
        Assert.Contains("UNHEX(REPLACE(`v`.`value`, '-', ''))", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` binary(16) PATH '$'", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// A model-to-TimeSpan converter retains elapsed values above 24 hours by
    /// extracting the provider representation as text.
    /// </summary>
    [Fact]
    public void Value_object_to_timespan_parameter_collection_uses_provider_representation()
    {
        using var context = CreateContext();
        ElapsedValue[] values = [new(TimeSpan.FromHours(27))];

        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Contains(item.ConvertedDuration))
            .ToQueryString();

        Assert.Contains("`value` longtext PATH '$'", sql, StringComparison.Ordinal);
        Assert.Contains("LENGTH(REPLACE(`v`.`value`, ':', ''))", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` time(0) PATH '$'", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// A DateTimeOffset-to-DateTime converter widens the intermediate mapping
    /// instead of inheriting the property's fractional-seconds precision.
    /// </summary>
    [Fact]
    public void DateTimeOffset_to_datetime_parameter_collection_uses_provider_representation()
    {
        using var context = CreateContext();
        DateTimeOffset[] values =
        [
            new(2026, 9, 19, 12, 34, 56, TimeSpan.FromHours(2)),
        ];

        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Contains(item.ConvertedTimestamp))
            .ToQueryString();

        Assert.Contains("`value` datetime(6) PATH '$'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` datetime(0) PATH '$'", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// A model-to-binary converter keeps Base64 transport textual until the
    /// final relational comparison.
    /// </summary>
    [Fact]
    public void Value_object_to_binary_parameter_collection_uses_provider_representation()
    {
        using var context = CreateContext();
        BinaryValue[] values = [new("alpha")];

        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Contains(item.ConvertedPayload))
            .ToQueryString();

        Assert.Contains("`value` longtext PATH '$'", sql, StringComparison.Ordinal);
        Assert.Contains("FROM_BASE64(`v`.`value`)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` varbinary(8) PATH '$'", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Decimal collection elements must not inherit precision or scale that
    /// can round a parameter into equality before comparison.
    /// </summary>
    [Fact]
    public void Decimal_parameter_collection_uses_lossless_extraction_facets()
    {
        using var context = CreateContext();
        var values = new[] { 3.14159m };

        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Count(value => value == item.Price) > 0)
            .ToQueryString();

        Assert.Contains("`value` decimal(65,30) PATH '$'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` decimal(10,1) PATH '$'", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// DateTime collection elements must retain microseconds even when the
    /// compared property stores whole seconds.
    /// </summary>
    [Fact]
    public void DateTime_parameter_collection_uses_lossless_extraction_facets()
    {
        using var context = CreateContext();
        var values = new[]
        {
            new DateTime(2026, 9, 19, 12, 34, 56, 500, DateTimeKind.Unspecified),
        };

        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Count(value => value == item.OccurredAt) > 0)
            .ToQueryString();

        Assert.Contains("`value` datetime(6) PATH '$'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` datetime(0) PATH '$'", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// DateTime values compared with a date column require a valid, lossless
    /// intermediate type rather than the nonexistent date(6) form.
    /// </summary>
    [Fact]
    public void DateTime_parameter_collection_uses_datetime_for_date_columns()
    {
        using var context = CreateContext();
        var values = new[] { new DateTime(2000, 1, 2, 12, 0, 0, DateTimeKind.Unspecified) };

        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Count(value => value == item.BirthDate) > 0)
            .ToQueryString();

        Assert.Contains("`value` datetime(6) PATH '$'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` date(6) PATH '$'", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Timestamp properties use a timezone-neutral, precision-six
    /// intermediate before the final timestamp comparison.
    /// </summary>
    [Fact]
    public void DateTime_parameter_collection_uses_datetime_for_timestamp_columns()
    {
        using var context = CreateContext();
        var values = new[]
        {
            new DateTime(2026, 9, 19, 12, 34, 56, 500, DateTimeKind.Utc),
        };

        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Count(value => value == item.RecordedAt) > 0)
            .ToQueryString();

        Assert.Contains("`value` datetime(6) PATH '$'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` timestamp(0) PATH '$'", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// DateOnly parameters retain their date representation without an
    /// unsupported fractional-seconds suffix.
    /// </summary>
    [Fact]
    public void DateOnly_parameter_collection_uses_date_extraction()
    {
        using var context = CreateContext();
        var values = new[] { new DateOnly(2026, 9, 19) };

        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Contains(item.CalendarDate))
            .ToQueryString();

        Assert.Contains("`value` date PATH '$'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` date(", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// TimeOnly collection elements must retain microseconds even when the
    /// compared property stores whole seconds.
    /// </summary>
    [Fact]
    public void TimeOnly_parameter_collection_uses_lossless_extraction_facets()
    {
        using var context = CreateContext();
        var values = new[] { new TimeOnly(12, 34, 56, 500) };

        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Count(value => value == item.StartsAt) > 0)
            .ToQueryString();

        Assert.Contains("`value` time(6) PATH '$'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` time(0) PATH '$'", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// TimeSpan collection elements must retain microseconds even when the
    /// compared property stores whole seconds.
    /// </summary>
    [Fact]
    public void TimeSpan_parameter_collection_uses_lossless_extraction_facets()
    {
        using var context = CreateContext();
        var values = new[] { TimeSpan.FromHours(27).Add(TimeSpan.FromMilliseconds(500)) };

        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Count(value => value == item.Duration) > 0)
            .ToQueryString();

        Assert.Contains("`value` longtext PATH '$'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` time(0) PATH '$'", sql, StringComparison.Ordinal);
        Assert.Contains("LENGTH(REPLACE(`v`.`value`, ':', ''))", sql, StringComparison.Ordinal);
        Assert.Contains("SUBSTRING_INDEX(`v`.`value`, ':', 1)", sql, StringComparison.Ordinal);
        Assert.Contains("CONCAT(", sql, StringComparison.Ordinal);
        Assert.Contains("CAST(CASE", sql, StringComparison.Ordinal);
        Assert.Contains("END AS TIME(6))", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Deferred byte-array elements must preserve their JSON Base64 decoding.
    /// </summary>
    [Fact]
    public void Byte_array_parameter_collection_decodes_json_text_after_type_inference()
    {
        using var context = CreateContext();
        byte[][] values = [[1, 2, 3], [4, 5, 6]];

        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Contains(item.Payload))
            .ToQueryString();

        Assert.Contains("`value` longtext PATH '$'", sql, StringComparison.Ordinal);
        Assert.Contains("FROM_BASE64(`v`.`value`)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` varbinary(32) PATH '$'", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// A model-mapped GUID collection retains the same decoding when its
    /// element mapping is known during initial translation.
    /// </summary>
    [Fact]
    public void Model_guid_collection_decodes_json_text_with_early_mapping()
    {
        using var context = CreateContext();

        var sql = context
            .Items
            .Where(item => item.GuidValues.Contains(s_first))
            .ToQueryString();

        Assert.Contains("`value` char(36) PATH '$'", sql, StringComparison.Ordinal);
        Assert.Contains("UNHEX(REPLACE(`g`.`value`, '-', ''))", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` binary(16) PATH '$'", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// A model-mapped Char36 GUID collection applies textual decoding when its
    /// element mapping is known during initial translation.
    /// </summary>
    [Fact]
    public void Model_char_guid_collection_preserves_text_with_early_mapping()
    {
        using var context = CreateContext(MySqlGuidFormat.Char36);

        var sql = context
            .Items
            .Where(item => item.GuidValues.Contains(s_first))
            .ToQueryString();

        Assert.Contains("`value` char(36) PATH '$'", sql, StringComparison.Ordinal);
        Assert.Contains("JSON_UNQUOTE(JSON_QUOTE(`g`.`value`))", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("UNHEX(", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` binary(16) PATH '$'", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// A model-mapped string collection retains its document representation;
    /// Base64 is reserved for parameter transport.
    /// </summary>
    [Fact]
    public void Model_string_collection_does_not_use_parameter_transport_encoding()
    {
        using var context = CreateContext();

        var sql = context
            .Items
            .Where(item => item.StringValues.Contains("alpha"))
            .ToQueryString();

        Assert.Contains("JSON_UNQUOTE(JSON_QUOTE(`s`.`value`))", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("FROM_BASE64", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Transport mappings are memoized per inferred source mapping so repeated
    /// query compilation preserves EF Core's mapping-cache identity.
    /// </summary>
    [Theory]
    [InlineData(nameof(PrimitiveCollectionEntity.Name))]
    [InlineData(nameof(PrimitiveCollectionEntity.ConvertedName))]
    [InlineData(nameof(PrimitiveCollectionEntity.Status))]
    [InlineData(nameof(PrimitiveCollectionEntity.ConvertedText))]
    [InlineData(nameof(PrimitiveCollectionEntity.Duration))]
    [InlineData(nameof(PrimitiveCollectionEntity.ConvertedDuration))]
    public void Parameter_transport_mapping_is_reused_for_the_same_inferred_mapping(
        string propertyName
    )
    {
        using var context = CreateContext();
        var typeMappingSource = context.GetService<IRelationalTypeMappingSource>();
        var sourceMapping = context.Model
            .FindEntityType(typeof(PrimitiveCollectionEntity))!
            .FindProperty(propertyName)!
            .GetRelationalTypeMapping();

        var first = MySqlJsonTableValueEncoding.GetParameterElementTypeMapping(
            sourceMapping,
            typeMappingSource);
        var second = MySqlJsonTableValueEncoding.GetParameterElementTypeMapping(
            sourceMapping,
            typeMappingSource);

        Assert.NotSame(sourceMapping, first);
        Assert.Same(first, second);
    }

    /// <summary>
    /// Element mappings without a custom parameter transport retain their
    /// original identity instead of entering the transport cache.
    /// </summary>
    [Fact]
    public void Unchanged_parameter_mapping_retains_source_identity()
    {
        using var context = CreateContext();
        var typeMappingSource = context.GetService<IRelationalTypeMappingSource>();
        var sourceMapping = context.Model
            .FindEntityType(typeof(PrimitiveCollectionEntity))!
            .FindProperty(nameof(PrimitiveCollectionEntity.Price))!
            .GetRelationalTypeMapping();

        var actual = MySqlJsonTableValueEncoding.GetParameterElementTypeMapping(
            sourceMapping,
            typeMappingSource);

        Assert.Same(sourceMapping, actual);
    }

    private static PrimitiveCollectionContext CreateContext(
        MySqlGuidFormat defaultGuidFormat = MySqlGuidFormat.Binary16
    )
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<PrimitiveCollectionContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)),
            options => options.DefaultGuidFormat(defaultGuidFormat));

        return new PrimitiveCollectionContext(builder.Options);
    }

    private sealed class PrimitiveCollectionContext(
        DbContextOptions<PrimitiveCollectionContext> options
    ) : DbContext(options)
    {
        public DbSet<PrimitiveCollectionEntity> Items => Set<PrimitiveCollectionEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<PrimitiveCollectionEntity>(entity =>
            {
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.BinaryId)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Binary16);
                entity
                    .Property(item => item.OptionalBinaryId)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Binary16);
                entity
                    .Property(item => item.OptionalCharId)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Char36);
                entity
                    .Property(item => item.CharId)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Char36);
                entity
                    .Property(item => item.Name)
                    .HasMaxLength(64)
                    .UseCollation("utf8mb4_bin");
                entity
                    .Property(item => item.OptionalName)
                    .HasMaxLength(64)
                    .UseCollation("utf8mb4_bin");
                entity
                    .Property(item => item.ConvertedName)
                    .HasConversion(
                        value => "db-" + value,
                        value => value.Substring(3))
                    .HasMaxLength(64)
                    .UseCollation("utf8mb4_bin");
                entity
                    .Property(item => item.ConvertedText)
                    .HasConversion(
                        value => value.Value,
                        value => new TextValue(value))
                    .HasMaxLength(64)
                    .UseCollation("utf8mb4_bin");
                entity
                    .Property(item => item.Status)
                    .HasConversion<string>()
                    .HasMaxLength(16)
                    .UseCollation("utf8mb4_bin");
                entity
                    .Property(item => item.OptionalStatus)
                    .HasConversion<string>()
                    .HasMaxLength(16)
                    .UseCollation("utf8mb4_bin");
                entity
                    .Property(item => item.ConvertedPrice)
                    .HasConversion(
                        value => value.Value,
                        value => new PriceValue(value))
                    .HasPrecision(10, 1);
                entity
                    .Property(item => item.ConvertedGuid)
                    .HasConversion(
                        value => value.Value,
                        value => new StrongGuid(value))
                    .HasColumnType("binary(16)");
                entity
                    .Property(item => item.ConvertedDuration)
                    .HasConversion(
                        value => value.Value,
                        value => new ElapsedValue(value))
                    .HasPrecision(0);
                entity
                    .Property(item => item.ConvertedTimestamp)
                    .HasConversion(
                        value => value.UtcDateTime,
                        value => new DateTimeOffset(value, TimeSpan.Zero))
                    .HasPrecision(0);
                entity
                    .Property(item => item.ConvertedPayload)
                    .HasConversion(
                        value => System.Text.Encoding.UTF8.GetBytes(value.Value),
                        value => new BinaryValue(System.Text.Encoding.UTF8.GetString(value)))
                    .HasColumnType("varbinary(8)");
                entity
                    .Property(item => item.Price)
                    .HasPrecision(10, 1);
                entity
                    .Property(item => item.OccurredAt)
                    .HasPrecision(0);
                entity
                    .Property(item => item.BirthDate)
                    .HasColumnType("date");
                entity
                    .Property(item => item.RecordedAt)
                    .HasColumnType("timestamp(0)");
                entity
                    .Property(item => item.CalendarDate)
                    .HasColumnType("date");
                entity
                    .Property(item => item.StartsAt)
                    .HasPrecision(0);
                entity
                    .Property(item => item.Duration)
                    .HasPrecision(0);
                entity
                    .Property(item => item.Payload)
                    .HasColumnType("varbinary(32)");
                entity
                    .PrimitiveCollection(item => item.GuidValues)
                    .HasColumnType("json");
                entity
                    .PrimitiveCollection(item => item.StringValues)
                    .HasColumnType("json");
            });
        }
    }

    private sealed class PrimitiveCollectionEntity
    {
        public int Id { get; set; }

        public Guid BinaryId { get; set; }

        public Guid? OptionalBinaryId { get; set; }

        public Guid? OptionalCharId { get; set; }

        public Guid CharId { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? OptionalName { get; set; }

        public string ConvertedName { get; set; } = string.Empty;

        public TextValue ConvertedText { get; set; }

        public CollectionStatus Status { get; set; }

        public CollectionStatus? OptionalStatus { get; set; }

        public PriceValue ConvertedPrice { get; set; }

        public StrongGuid ConvertedGuid { get; set; }

        public ElapsedValue ConvertedDuration { get; set; }

        public DateTimeOffset ConvertedTimestamp { get; set; }

        public BinaryValue ConvertedPayload { get; set; }

        public decimal Price { get; set; }

        public DateTime OccurredAt { get; set; }

        public DateTime BirthDate { get; set; }

        public DateTime RecordedAt { get; set; }

        public DateOnly CalendarDate { get; set; }

        public TimeOnly StartsAt { get; set; }

        public TimeSpan Duration { get; set; }

        public byte[] Payload { get; set; } = [];

        public Guid[] GuidValues { get; set; } = [];

        public string[] StringValues { get; set; } = [];
    }

    private enum CollectionStatus
    {
        Inactive,
        Active,
    }

    private readonly record struct PriceValue(decimal Value);

    private readonly record struct StrongGuid(Guid Value);

    private readonly record struct ElapsedValue(TimeSpan Value);

    private readonly record struct TextValue(string Value);

    private readonly record struct BinaryValue(string Value);
}
