namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies provider-specific translation of parameterized primitive collections.
/// </summary>
public sealed partial class MySqlPrimitiveCollectionParameterTests
{
    private static readonly Guid s_first = Guid.Parse("00000001-0001-0002-0304-05060708090a");
    private static readonly Guid s_second = Guid.Parse("00000002-0002-0003-0405-060708090a0b");

    /// <summary>
    /// Contexts of one CLR type with different collection modes must compile
    /// their own query shape regardless of which mode executes first.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Collection_modes_do_not_share_a_compiled_query(
        bool parameterFirst
    )
    {
        // Arrange
        var firstMode = parameterFirst
            ? ParameterTranslationMode.Parameter
            : ParameterTranslationMode.MultipleParameters;

        var secondMode = parameterFirst
            ? ParameterTranslationMode.MultipleParameters
            : ParameterTranslationMode.Parameter;

        var values = new[] { s_first, s_second };

        // This test must use EF Core's shared internal-provider cache. The
        // normal functional helper disables it and could not catch cache bleed.
        using var first = CreateContext(parameterTranslationMode: firstMode, cacheServiceProvider: true);
        using var second = CreateContext(parameterTranslationMode: secondMode, cacheServiceProvider: true);

        // Act
        var firstSql = Query(first, values).ToQueryString();
        var secondSql = Query(second, values).ToQueryString();

        // Assert
        Assert.Equal(parameterFirst, firstSql.Contains("JSON_TABLE", StringComparison.Ordinal));
        Assert.Equal(!parameterFirst, secondSql.Contains("JSON_TABLE", StringComparison.Ordinal));
    }

    /// <summary>
    /// A deferred string mapping must remain visible to EF Core's inference
    /// pass and preserve arbitrary SQL text after extraction.
    /// </summary>
    [Fact]
    public void String_parameter_collection_decodes_text_after_type_inference()
    {
        // Arrange
        using var context = CreateContext();
        string[] values = ["alpha", "\"quoted\"", "\"\\q\""];

        // Act
        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Contains(item.Name))
            .ToQueryString();

        // Assert
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
        // Arrange
        using var context = CreateContext();
        string?[] values = ["alpha", null];

        // Act
        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Contains(item.OptionalName))
            .ToQueryString();

        // Assert
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
        // Arrange
        using var context = CreateContext();
        string[] values = ["alpha"];

        // Act
        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Contains(item.ConvertedName))
            .ToQueryString();

        // Assert
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
        // Arrange
        using var context = CreateContext();
        CollectionStatus[] values = [CollectionStatus.Active];

        // Act
        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Contains(item.Status))
            .ToQueryString();

        // Assert
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
        // Arrange
        using var context = CreateContext();
        CollectionStatus?[] values = [CollectionStatus.Active, null];

        // Act
        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Contains(item.OptionalStatus))
            .ToQueryString();

        // Assert
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
        // Arrange
        using var context = CreateContext();
        TextValue[] values = [new("\"\\q\"")];

        // Act
        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Contains(item.ConvertedText))
            .ToQueryString();

        // Assert
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
        // Arrange
        using var context = CreateContext();
        PriceValue[] values = [new(3.14159m)];

        // Act
        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Contains(item.ConvertedPrice))
            .ToQueryString();

        // Assert
        Assert.Contains("`value` decimal(65,30) PATH '$'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` decimal(10,1) PATH '$'", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// A model-to-TimeSpan converter retains elapsed values above 24 hours by
    /// extracting the provider representation as text.
    /// </summary>
    [Fact]
    public void Value_object_to_timespan_parameter_collection_uses_provider_representation()
    {
        // Arrange
        using var context = CreateContext();
        ElapsedValue[] values = [new(TimeSpan.FromHours(27))];

        // Act
        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Contains(item.ConvertedDuration))
            .ToQueryString();

        // Assert
        Assert.Contains("`value` longtext PATH '$'", sql, StringComparison.Ordinal);
        Assert.Contains("LENGTH(REPLACE(`v`.`value`, ':', ''))", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` time(0) PATH '$'", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// A model-to-binary converter keeps Base64 transport textual until the
    /// final relational comparison.
    /// </summary>
    [Fact]
    public void Value_object_to_binary_parameter_collection_uses_provider_representation()
    {
        // Arrange
        using var context = CreateContext();
        BinaryValue[] values = [new("alpha")];

        // Act
        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Contains(item.ConvertedPayload))
            .ToQueryString();

        // Assert
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
        // Arrange
        using var context = CreateContext();
        var values = new[] { 3.14159m };

        // Act
        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Count(value => value == item.Price) > 0)
            .ToQueryString();

        // Assert
        Assert.Contains("`value` decimal(65,30) PATH '$'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` decimal(10,1) PATH '$'", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// DateOnly parameters retain their date representation without an
    /// unsupported fractional-seconds suffix.
    /// </summary>
    [Fact]
    public void DateOnly_parameter_collection_uses_date_extraction()
    {
        // Arrange
        using var context = CreateContext();
        var values = new[] { new DateOnly(2026, 9, 19) };

        // Act
        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Contains(item.CalendarDate))
            .ToQueryString();

        // Assert
        Assert.Contains("`value` date PATH '$'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` date(", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// TimeSpan collection elements must retain microseconds even when the
    /// compared property stores whole seconds.
    /// </summary>
    [Fact]
    public void TimeSpan_parameter_collection_uses_lossless_extraction_facets()
    {
        // Arrange
        using var context = CreateContext();
        var values = new[] { TimeSpan.FromHours(27).Add(TimeSpan.FromMilliseconds(500)) };

        // Act
        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Count(value => value == item.Duration) > 0)
            .ToQueryString();

        // Assert
        Assert.Contains("`value` longtext PATH '$'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` time(0) PATH '$'", sql, StringComparison.Ordinal);
        Assert.Contains("LENGTH(REPLACE(`v`.`value`, ':', ''))", sql, StringComparison.Ordinal);
        Assert.Contains("SUBSTRING_INDEX(`v`.`value`, ':', 1)", sql, StringComparison.Ordinal);
        Assert.Contains("CONCAT(", sql, StringComparison.Ordinal);
        Assert.Contains("CAST(CASE", sql, StringComparison.Ordinal);
        Assert.Contains("END AS TIME(6))", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// A model-mapped string collection retains its document representation;
    /// Base64 is reserved for parameter transport.
    /// </summary>
    [Fact]
    public void Model_string_collection_does_not_use_parameter_transport_encoding()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items
            .Where(item => item.StringValues.Contains("alpha"))
            .ToQueryString();

        // Assert
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
        // Arrange
        using var context = CreateContext();
        var typeMappingSource = context.GetService<IRelationalTypeMappingSource>();
        var sourceMapping = context.Model
            .FindEntityType(typeof(PrimitiveCollectionEntity))!
            .FindProperty(propertyName)!
            .GetRelationalTypeMapping();

        var first = MySqlJsonTableValueEncoding.GetParameterElementTypeMapping(
            sourceMapping,
            typeMappingSource);

        // Act
        var second = MySqlJsonTableValueEncoding.GetParameterElementTypeMapping(
            sourceMapping,
            typeMappingSource);

        // Assert
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
        // Arrange
        using var context = CreateContext();
        var typeMappingSource = context.GetService<IRelationalTypeMappingSource>();
        var sourceMapping = context.Model
            .FindEntityType(typeof(PrimitiveCollectionEntity))!
            .FindProperty(nameof(PrimitiveCollectionEntity.Price))!
            .GetRelationalTypeMapping();

        // Act
        var actual = MySqlJsonTableValueEncoding.GetParameterElementTypeMapping(
            sourceMapping,
            typeMappingSource);

        // Assert
        Assert.Same(sourceMapping, actual);
    }
}
