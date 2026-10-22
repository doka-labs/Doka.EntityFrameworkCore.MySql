namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Contains the temporal responsibility for MySqlTypeMappingBaselineTests.</summary>
public sealed partial class MySqlTypeMappingBaselineTests
{
    /// <summary>
    /// Verifies that time literals are truncated to the declared store
    /// precision before either engine can apply a different rounding policy.
    /// </summary>
    [Theory]
    [InlineData(typeof(TimeOnly), "time(6)", "TIME '12:34:56.123456'")]
    [InlineData(typeof(TimeOnly), "time(3)", "TIME '12:34:56.123'")]
    [InlineData(typeof(TimeOnly), "time", "TIME '12:34:56'")]
    [InlineData(typeof(TimeSpan), "time(6)", "'27:00:00.123456'")]
    [InlineData(typeof(TimeSpan), "time", "'27:00:00'")]
    public void Time_literals_follow_the_declared_microsecond_precision(
        Type clrType,
        string storeType,
        string expected
    )
    {
        // Arrange
        using var context = new TypeMappingContext(CreateOptions<TypeMappingContext>());
        var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(clrType, storeType)!;
        var fraction = TimeSpan.FromTicks(1_234_567);
        object value = clrType == typeof(TimeOnly)
            ? new TimeOnly(12, 34, 56).Add(fraction)
            : TimeSpan.FromHours(27) + fraction;

        // Act
        var literal = mapping.GenerateSqlLiteral(value);

        // Assert
        Assert.Equal(expected, literal);
    }

    /// <summary>
    /// TimeSpan document persistence retains EF Core's unrestricted JSON
    /// representation; the MySQL TIME range applies only at relational and
    /// parameter-collection boundaries.
    /// </summary>
    [Fact]
    public void TimeSpan_mapping_preserves_the_standard_json_reader_writer()
    {
        // Arrange
        using var context = new TypeMappingContext(CreateOptions<TypeMappingContext>());

        // Act
        var mapping = context
            .GetService<IRelationalTypeMappingSource>()
            .FindMapping(typeof(TimeSpan), "time(6)");

        // Assert
        Assert.NotNull(mapping);
        Assert.Same(
            JsonTimeSpanReaderWriter.Instance,
            mapping.JsonValueReaderWriter);
    }

    /// <summary>Exposes independent temporal literal shapes at every server precision.</summary>
    public static TheoryData<string, int, string> TimeLiteralCases
    {
        get
        {
            var cases = new TheoryData<string, int, string>();

            for (var precision = 0; precision <= 6; precision++)
            {
                var fraction = precision == 0 ? string.Empty : "." + "123456"[..precision];

                cases.Add("time-only", precision, $"TIME '12:34:56{fraction}'");
                cases.Add("positive-duration", precision, $"'27:00:00{fraction}'");
                cases.Add("negative-duration", precision, $"'-27:00:00{fraction}'");
            }

            return cases;
        }
    }

    /// <summary>Each temporal shape truncates the same input independently at its declared precision.</summary>
    [Theory]
    [MemberData(nameof(TimeLiteralCases))]
    public void Time_literals_cover_every_supported_precision(
        string shape,
        int precision,
        string expected
    )
    {
        // Arrange
        RelationalTypeMapping mapping = shape == "time-only"
            ? new MySqlTimeOnlyTypeMapping($"time({precision})", precision)
            : new MySqlTimeSpanTypeMapping($"time({precision})", precision);

        var fraction = TimeSpan.FromTicks(1_234_567);
        object value = shape switch
        {
            "time-only" => new TimeOnly(12, 34, 56).Add(fraction),
            "positive-duration" => TimeSpan.FromHours(27) + fraction,
            "negative-duration" => -(TimeSpan.FromHours(27) + fraction),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        // Act
        var literal = mapping.GenerateSqlLiteral(value);

        // Assert
        Assert.Equal(expected, literal);
    }

    /// <summary>Culture changes do not affect a time literal or turn truncated negative zero into a signed value.</summary>
    [Theory]
    [InlineData(true, "TIME '23:59:59.999999'")]
    [InlineData(false, "'00:00:00.000000'")]
    public void Temporal_literals_are_culture_independent_and_canonicalize_negative_zero(
        bool timeOnly,
        string expected
    )
    {
        // Arrange
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        RelationalTypeMapping mapping = timeOnly
            ? new MySqlTimeOnlyTypeMapping("time(6)", 6)
            : new MySqlTimeSpanTypeMapping("time(6)", 6);

        object value = timeOnly ? TimeOnly.MaxValue : TimeSpan.FromTicks(-9);

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");

            // Act
            var literal = mapping.GenerateSqlLiteral(value);

            // Assert
            Assert.Equal(expected, literal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    /// <summary>
    /// Verifies that both CLR time mappings expose EF Core's compiled-model
    /// defaults and preserve precision when cloned from those defaults.
    /// </summary>
    [Theory]
    [InlineData(typeof(TimeOnly), "TIME '12:34:56.123'")]
    [InlineData(typeof(TimeSpan), "'12:34:56.123'")]
    public void Time_mappings_expose_compiled_model_defaults_and_clone_precision(
        Type clrType,
        string expected
    )
    {
        // Arrange
        using var context = new TypeMappingContext(CreateOptions<TypeMappingContext>());
        var typeMappingSource = context.GetService<IRelationalTypeMappingSource>();
        var value = new TimeOnly(12, 34, 56).Add(TimeSpan.FromTicks(1_234_567));
        RelationalTypeMapping expectedMapping = clrType == typeof(TimeOnly)
            ? MySqlTimeOnlyTypeMapping.Default
            : MySqlTimeSpanTypeMapping.Default;

        object input = clrType == typeof(TimeOnly) ? value : value.ToTimeSpan();

        // Act
        var mapping = typeMappingSource.FindMapping(clrType)!;
        var clone = mapping.WithPrecisionAndScale(3, null);
        var literal = clone.GenerateSqlLiteral(input);

        // Assert
        Assert.Same(expectedMapping, mapping);
        Assert.Equal(6, mapping.Precision);
        Assert.Equal(expected, literal);
    }

    /// <summary>
    /// Verifies that both signed boundaries of the MySQL TIME domain remain
    /// representable after microsecond truncation.
    /// </summary>
    [Theory]
    [InlineData(false, 0, "'838:59:59.000000'")]
    [InlineData(false, 9, "'838:59:59.000000'")]
    [InlineData(true, 0, "'-838:59:59.000000'")]
    [InlineData(true, 9, "'-838:59:59.000000'")]
    public void TimeSpan_literals_accept_both_supported_server_boundaries(
        bool negative,
        int additionalTicks,
        string expected
    )
    {
        // Arrange
        using var context = new TypeMappingContext(CreateOptions<TypeMappingContext>());
        var typeMapping = context
            .GetService<IRelationalTypeMappingSource>()
            .FindMapping(typeof(TimeSpan), "time(6)")!;

        var maximum = TimeSpan.FromHours(838)
            + TimeSpan.FromMinutes(59)
            + TimeSpan.FromSeconds(59)
            + TimeSpan.FromTicks(additionalTicks);

        var value = negative ? -maximum : maximum;

        // Act
        var literal = typeMapping.GenerateSqlLiteral(value);

        // Assert
        Assert.Equal(expected, literal);
    }

    /// <summary>
    /// Verifies that neither signed side can generate a literal outside the
    /// MySQL TIME domain.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TimeSpan_literals_reject_values_outside_both_server_boundaries(
        bool negative
    )
    {
        // Arrange
        using var context = new TypeMappingContext(CreateOptions<TypeMappingContext>());
        var typeMapping = context
            .GetService<IRelationalTypeMappingSource>()
            .FindMapping(typeof(TimeSpan), "time(6)")!;

        var maximum = TimeSpan.FromHours(838)
            + TimeSpan.FromMinutes(59)
            + TimeSpan.FromSeconds(59);

        var outsideMaximum = maximum + TimeSpan.FromTicks(10);
        var value = negative ? -outsideMaximum : outsideMaximum;

        // Act
        var exception = Record.Exception(() => typeMapping.GenerateSqlLiteral(value));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    /// <summary>
    /// Verifies that unsupported server precision is rejected before an
    /// invalid temporal literal can enter generated SQL.
    /// </summary>
    [Theory]
    [InlineData(typeof(DateTime), "datetime(7)")]
    [InlineData(typeof(DateTime), "timestamp(7)")]
    [InlineData(typeof(TimeOnly), "time(7)")]
    [InlineData(typeof(TimeSpan), "time(7)")]
    public void Temporal_mapping_source_rejects_fractional_precision_above_six(
        Type clrType,
        string storeType
    )
    {
        // Arrange
        using var context = new TypeMappingContext(CreateOptions<TypeMappingContext>());
        var typeMappingSource = context.GetService<IRelationalTypeMappingSource>();

        // Act
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            typeMappingSource.FindMapping(clrType, storeType));

        // Assert
        Assert.Equal("precision", exception.ParamName);
        Assert.Equal(7, exception.ActualValue);
    }

    /// <summary>
    /// Verifies that cloning cannot bypass DateTime precision validation.
    /// </summary>
    [Fact]
    public void Datetime_mapping_clone_rejects_fractional_precision_above_six()
    {
        // Arrange
        var mapping = MySqlDateTimeTypeMapping.Default;

        // Act
        var exception = Record.Exception(() => mapping.WithPrecisionAndScale(7, null));

        // Assert
        var argumentException = Assert.IsType<ArgumentOutOfRangeException>(exception);

        Assert.Equal("precision", argumentException.ParamName);
        Assert.Equal(7, argumentException.ActualValue);
    }

    /// <summary>
    /// Verifies that public time-mapping constructors reject negative precision.
    /// </summary>
    [Theory]
    [InlineData(typeof(MySqlTimeOnlyTypeMapping))]
    [InlineData(typeof(MySqlTimeSpanTypeMapping))]
    public void Time_mapping_constructors_reject_negative_precision(
        Type mappingType
    )
    {
        // Arrange
        const int invalidPrecision = -1;

        // Act
        var exception = Record.Exception(() =>
        {
            _ = mappingType == typeof(MySqlTimeOnlyTypeMapping)
                ? new MySqlTimeOnlyTypeMapping("time", invalidPrecision)
                : (RelationalTypeMapping)new MySqlTimeSpanTypeMapping("time", invalidPrecision);
        });

        // Assert
        var argumentException = Assert.IsType<ArgumentOutOfRangeException>(exception);

        Assert.Equal("precision", argumentException.ParamName);
        Assert.Equal(invalidPrecision, argumentException.ActualValue);
    }

    /// <summary>
    /// Verifies every fractional-seconds precision supported by both MySQL and
    /// MariaDB across the provider's DateTime, TimeOnly, and TimeSpan mappings.
    /// </summary>
    [Theory]
    [MemberData(nameof(TemporalMappingPrecisionCases))]
    public void Temporal_mappings_accept_every_supported_precision(
        Type clrType,
        string storeType,
        int precision
    )
    {
        // Arrange
        using var context = new TypeMappingContext(CreateOptions<TypeMappingContext>());
        var typeMappingSource = context.GetService<IRelationalTypeMappingSource>();
        var expected = $"{storeType}({precision})";

        // Act
        var mapping = typeMappingSource.FindMapping(clrType, expected);

        // Assert
        Assert.Equal(expected, mapping?.StoreType);
    }

    /// <summary>Lists each CLR/store-type pair at every supported fractional-seconds precision.</summary>
    public static TheoryData<Type, string, int> TemporalMappingPrecisionCases
    {
        get
        {
            var cases = new TheoryData<Type, string, int>();

            for (var precision = 0; precision <= 6; precision++)
            {
                cases.Add(typeof(DateTime), "datetime", precision);
                cases.Add(typeof(DateTime), "timestamp", precision);
                cases.Add(typeof(TimeOnly), "time", precision);
                cases.Add(typeof(TimeSpan), "time", precision);
            }

            return cases;
        }
    }

    /// <summary>
    /// Verifies that fluent DateTime precision from zero through six produces
    /// the corresponding provider mapping without opening a connection.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Fluent_datetime_precision_accepts_every_supported_value(
        int precision
    )
    {
        // Arrange
        var connectionProbe = new ConnectionOpeningProbe();
        var builder = CreateTemporalPrecisionOptions(connectionProbe);
        using var context = new TemporalPrecisionContext(builder.Options, precision, storeType: null);

        // Act
        var property = context.Model
            .FindEntityType(typeof(TemporalPrecisionEntity))!
            .FindProperty(nameof(TemporalPrecisionEntity.Value))!;

        // Assert
        Assert.Equal($"datetime({precision})", property.GetColumnType());
        Assert.Equal(0, connectionProbe.OpenCount);
    }

    /// <summary>
    /// Verifies that temporal store types without a precision facet remain
    /// valid and retain their explicit store type.
    /// </summary>
    [Theory]
    [InlineData(typeof(DateTime), "datetime")]
    [InlineData(typeof(DateTime), "timestamp")]
    [InlineData(typeof(TimeOnly), "time")]
    [InlineData(typeof(TimeSpan), "time")]
    public void Explicit_temporal_store_types_accept_an_omitted_precision(
        Type clrType,
        string storeType
    )
    {
        // Arrange
        using var context = new TypeMappingContext(CreateOptions<TypeMappingContext>());
        var typeMappingSource = context.GetService<IRelationalTypeMappingSource>();

        // Act
        var mapping = typeMappingSource.FindMapping(clrType, storeType);

        // Assert
        Assert.Equal(storeType, mapping?.StoreType);
    }

    /// <summary>
    /// Verifies that SQL-significant precision remains strict while harmless
    /// whitespace accepted by the engines does not become invalid metadata.
    /// </summary>
    [Theory]
    [InlineData(typeof(DateTime), "datetime( 3 )")]
    [InlineData(typeof(DateTime), "timestamp( 3 )")]
    [InlineData(typeof(TimeOnly), "time( 3 )")]
    [InlineData(typeof(TimeSpan), "time( 3 )")]
    public void Explicit_temporal_store_types_accept_whitespace_around_precision(
        Type clrType,
        string storeType
    )
    {
        // Arrange
        using var context = new TypeMappingContext(CreateOptions<TypeMappingContext>());
        var typeMappingSource = context.GetService<IRelationalTypeMappingSource>();

        // Act
        var mapping = typeMappingSource.FindMapping(clrType, storeType);

        // Assert
        Assert.Equal(storeType, mapping?.StoreType);
    }

    /// <summary>
    /// Verifies that partially formed or trailing precision metadata is not
    /// silently normalized into a different temporal store type.
    /// </summary>
    [Theory]
    [InlineData(typeof(DateTime), "datetime()")]
    [InlineData(typeof(DateTime), "datetime(x)")]
    [InlineData(typeof(DateTime), "datetime(-1)")]
    [InlineData(typeof(DateTime), "datetime(1")]
    [InlineData(typeof(DateTime), "datetime(1) trailing")]
    [InlineData(typeof(DateTime), "timestamp((1))")]
    [InlineData(typeof(TimeOnly), "time()")]
    [InlineData(typeof(TimeOnly), "time(x)")]
    [InlineData(typeof(TimeSpan), "time(1) trailing")]
    public void Explicit_temporal_store_types_reject_malformed_precision(
        Type clrType,
        string storeType
    )
    {
        // Arrange
        using var context = new TypeMappingContext(CreateOptions<TypeMappingContext>());
        var typeMappingSource = context.GetService<IRelationalTypeMappingSource>();

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() =>
            typeMappingSource.FindMapping(clrType, storeType));

        // Assert
        Assert.Contains(storeType, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that fluent DateTime precision and explicit temporal store
    /// types fail while the model is built, before a connection can open.
    /// </summary>
    [Theory]
    [InlineData(7, null)]
    [InlineData(null, "datetime(7)")]
    [InlineData(null, "timestamp(7)")]
    public void Invalid_datetime_precision_fails_before_database_io(
        int? precision,
        string? storeType
    )
    {
        // Arrange
        var connectionProbe = new ConnectionOpeningProbe();
        var builder = CreateTemporalPrecisionOptions(connectionProbe);
        using var context = new TemporalPrecisionContext(builder.Options, precision, storeType);

        // Act
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => _ = context.Model);

        // Assert
        Assert.Equal("precision", exception.ParamName);
        Assert.Equal(precision ?? 7, exception.ActualValue);
        Assert.Equal(0, connectionProbe.OpenCount);
    }

    /// <summary>
    /// Verifies that temporal precision on JSON collection elements remains
    /// model metadata instead of becoming an invalid relational store facet.
    /// </summary>
    [Theory]
    [InlineData(nameof(JsonTemporalElementEntity.DateTimes), "datetime(6)")]
    [InlineData(nameof(JsonTemporalElementEntity.Times), "time(6)")]
    [InlineData(nameof(JsonTemporalElementEntity.Durations), "time(6)")]
    public void Json_temporal_element_precision_does_not_declare_relational_precision(
        string propertyName,
        string expectedStoreType
    )
    {
        // Arrange
        using var context = new JsonTemporalElementPrecisionContext(
            CreateOptions<JsonTemporalElementPrecisionContext>());

        var elementType = context.Model
            .FindEntityType(typeof(JsonTemporalElementEntity))!
            .FindProperty(propertyName)!
            .GetElementType()!;

        // Act
        var mapping = Assert.IsAssignableFrom<RelationalTypeMapping>(elementType.GetTypeMapping());

        // Assert
        Assert.Equal(12, elementType.GetPrecision());
        Assert.Equal(expectedStoreType, mapping.StoreType);
    }

    /// <summary>
    /// Verifies that TimeSpanToTicksConverter produces bigint column.
    /// </summary>
    [Fact]
    public void Timespan_to_ticks_converter_produces_bigint_column()
    {
        // Arrange
        using var context = new ValueConverterContext(CreateOptions<ValueConverterContext>());

        // Act
        var entityType = context.Model.FindEntityType(typeof(ValueConverterEntity))!;

        // Assert
        Assert.Equal("bigint", entityType.FindProperty(nameof(ValueConverterEntity.DurationTicks))!.GetColumnType());
    }

    /// <summary>
    /// DateTimeOffset to DateTime custom converter produces datetime(6) column.
    /// </summary>
    [Fact]
    public void Datetimeoffset_to_datetime_converter_produces_datetime_column()
    {
        // Arrange
        using var context = new ValueConverterContext(CreateOptions<ValueConverterContext>());

        // Act
        var entityType = context.Model.FindEntityType(typeof(ValueConverterEntity))!;
        var columnType = entityType.FindProperty(nameof(ValueConverterEntity.LastModified))!.GetColumnType();

        // Assert
        Assert.Equal("datetime(6)", columnType);
    }
}
