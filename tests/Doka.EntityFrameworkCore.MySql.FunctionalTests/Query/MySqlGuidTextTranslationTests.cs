namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Pins GUID text translation to the actual storage mapping, independently of
/// the context default and the property's CLR type.
/// </summary>
public sealed class MySqlGuidTextTranslationTests
{
    private static readonly MySqlGuidFormat[] s_formats = [MySqlGuidFormat.Binary16, MySqlGuidFormat.Char36];
    private static readonly bool[] s_optionalVariants = [false, true];
    private static readonly string[] s_unsupportedProperties = ["CustomTextToken", "LittleEndianToken"];
    private static readonly string[] s_translationOperations = ["ToString", "Like"];
    private static readonly string[] s_textStoreTypes = ["char(36)", "varchar(36)"];
    private static readonly Type[] s_clrTypes = [typeof(Guid), typeof(string)];
    private static readonly string[] s_properties =
    [
        "DefaultToken",
        "BinaryToken",
        "TextToken",
        "BinaryColumnToken",
        "CharColumnToken",
        "VarCharColumnToken",
        "ConvertedTextToken",
    ];

    /// <summary>Identifies each storage mapping under both configured defaults.</summary>
    public static IEnumerable<object[]> PropertyCases =>
        from format in s_formats
        from property in s_properties
        select new object[] { format, property };

    /// <summary>Identifies required and escaped nullable LIKE operands independently.</summary>
    public static IEnumerable<object[]> LikeCases =>
        from format in s_formats
        from property in s_properties
        from optional in s_optionalVariants
        select new object[] { format, property, optional };

    /// <summary>Identifies each unsupported layout and formatting operation.</summary>
    public static IEnumerable<object[]> UnsupportedLayoutCases =>
        from format in s_formats
        from property in s_unsupportedProperties
        from operation in s_translationOperations
        select new object[] { format, property, operation };

    /// <summary>Identifies GUID and string store-type lookups separately.</summary>
    public static IEnumerable<object[]> StoreTypeCases =>
        from format in s_formats
        from storeType in s_textStoreTypes
        from clrType in s_clrTypes
        select new object[] { format, storeType, clrType };

    /// <summary>Verifies canonical formatting for one explicit or inferred mapping.</summary>
    [Theory]
    [MemberData(nameof(PropertyCases))]
    public void ToString_formats_mapping_as_canonical_text(
        MySqlGuidFormat defaultFormat,
        string propertyName
    )
    {
        // Arrange
        using var context = CreateContext(defaultFormat);
        var query = context.Set<GuidItem>()
            .Where(item => EF.Property<Guid>(item, propertyName).ToString()
                == "00112233-4455-6677-8899-aabbccddeeff");

        // Act
        var sql = query.ToQueryString();

        // Assert
        AssertTextMapping(sql, propertyName, defaultFormat, canonicalizeText: true);
    }

    /// <summary>Verifies binary formatting and text collation for one LIKE operand.</summary>
    [Theory]
    [MemberData(nameof(LikeCases))]
    public void Like_formats_binary_mapping_and_preserves_text_collation(
        MySqlGuidFormat defaultFormat,
        string propertyName,
        bool optional
    )
    {
        // Arrange
        using var context = CreateContext(defaultFormat);
        var operandName = optional ? $"Optional{propertyName}" : propertyName;
        var query = optional
            ? context.Set<GuidItem>()
                .Where(item => EF.Functions.Like(EF.Property<Guid?>(item, operandName), "00112233-%", "!"))
            : context.Set<GuidItem>()
                .Where(item => EF.Functions.Like(EF.Property<Guid>(item, operandName), "00112233-%"));

        // Act
        var sql = query.ToQueryString();

        // Assert
        AssertTextMapping(sql, operandName, defaultFormat, canonicalizeText: false);

        if (optional)
        {
            Assert.Contains("ESCAPE '!'", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("COALESCE(", sql, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Verifies the CLR contract of the default GUID mapping.</summary>
    [Theory]
    [InlineData(MySqlGuidFormat.Binary16)]
    [InlineData(MySqlGuidFormat.Char36)]
    public void Default_guid_mapping_retains_guid_CLR_type(
        MySqlGuidFormat defaultFormat
    )
    {
        // Arrange
        using var context = CreateContext(defaultFormat);
        var source = context.GetService<IRelationalTypeMappingSource>();

        // Act
        var mapping = source.FindMapping(typeof(Guid));

        // Assert
        Assert.NotNull(mapping);
        Assert.Equal(typeof(Guid), mapping.ClrType);
    }

    /// <summary>Verifies storage representation of an unformatted generated GUID.</summary>
    [Theory]
    [InlineData(MySqlGuidFormat.Binary16)]
    [InlineData(MySqlGuidFormat.Char36)]
    public void NewGuid_uses_the_context_default_storage_representation(
        MySqlGuidFormat defaultFormat
    )
    {
        // Arrange
        using var context = CreateContext(defaultFormat);
        var query = context.Set<GuidItem>().Select(item => Guid.NewGuid());

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("UUID()", sql, StringComparison.Ordinal);

        if (defaultFormat == MySqlGuidFormat.Binary16)
        {
            Assert.Contains("UNHEX(REPLACE(UUID()", sql, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("UNHEX(", sql, StringComparison.Ordinal);
        }
    }

    /// <summary>Verifies generated GUID text independently of default-mapping lookup.</summary>
    [Theory]
    [InlineData(MySqlGuidFormat.Binary16)]
    [InlineData(MySqlGuidFormat.Char36)]
    public void NewGuid_text_formats_the_context_default_storage_representation(
        MySqlGuidFormat defaultFormat
    )
    {
        // Arrange
        using var context = CreateContext(defaultFormat);
        var query = context.Set<GuidItem>().Select(item => Guid.NewGuid().ToString());

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("UUID()", sql, StringComparison.Ordinal);
        Assert.Equal(defaultFormat == MySqlGuidFormat.Binary16, sql.Contains("HEX(", StringComparison.Ordinal));
    }

    /// <summary>Rejects each unsupported converter layout for each formatting operation.</summary>
    [Theory]
    [MemberData(nameof(UnsupportedLayoutCases))]
    public void Guid_text_translation_rejects_unknown_or_incompatible_converter_layouts(
        MySqlGuidFormat defaultFormat,
        string propertyName,
        string operation
    )
    {
        // Arrange
        using var context = CreateContext(defaultFormat);
        var query = operation switch
        {
            "ToString" => context.Set<GuidItem>()
                .Where(item => EF.Property<Guid>(item, propertyName).ToString()
                    == "00112233-4455-6677-8899-aabbccddeeff"),
            "Like" => context.Set<GuidItem>()
                .Where(item => EF.Functions.Like(EF.Property<Guid>(item, propertyName), "00112233-%")),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        // Act
        var exception = Assert.Throws<InvalidOperationException>(query.ToQueryString);

        // Assert
        Assert.Contains("GUID text translation does not support", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Preserves a GUID parameter's default mapping without converting the pattern.</summary>
    [Theory]
    [InlineData(MySqlGuidFormat.Binary16)]
    [InlineData(MySqlGuidFormat.Char36)]
    public void Guid_parameters_use_the_context_default_without_converting_the_pattern(
        MySqlGuidFormat defaultFormat
    )
    {
        // Arrange
        using var context = CreateContext(defaultFormat);
        Guid? token = new Guid("00112233-4455-6677-8899-aabbccddeeff");
        var query = context.Set<GuidItem>().Where(item => EF.Functions.Like(token, item.Pattern));

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("LIKE `g`.`Pattern`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("HEX(`g`.`Pattern`)", sql, StringComparison.Ordinal);
        Assert.Equal(
            defaultFormat == MySqlGuidFormat.Binary16,
            sql.Contains("HEX(@token)", StringComparison.Ordinal));
    }

    /// <summary>Preserves constant GUID formatting independently of parameter formatting.</summary>
    [Theory]
    [InlineData(MySqlGuidFormat.Binary16)]
    [InlineData(MySqlGuidFormat.Char36)]
    public void Guid_constants_use_the_context_default_without_converting_the_pattern(
        MySqlGuidFormat defaultFormat
    )
    {
        // Arrange
        using var context = CreateContext(defaultFormat);
        var query = context.Set<GuidItem>()
            .Where(item => EF.Functions.Like(new Guid("00112233-4455-6677-8899-aabbccddeeff"), item.Pattern));

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("LIKE `g`.`Pattern`", sql, StringComparison.Ordinal);
        Assert.Contains(
            defaultFormat == MySqlGuidFormat.Binary16
                ? "HEX(X'00112233445566778899AABBCCDDEEFF')"
                : "'00112233-4455-6677-8899-aabbccddeeff' LIKE",
            sql,
            StringComparison.Ordinal);
    }

    /// <summary>Verifies that physical text storage does not merge GUID and string CLR contracts.</summary>
    [Theory]
    [MemberData(nameof(StoreTypeCases))]
    public void Text_store_type_lookups_keep_guid_and_string_CLR_contracts_separate(
        MySqlGuidFormat defaultFormat,
        string storeType,
        Type clrType
    )
    {
        // Arrange
        using var context = CreateContext(defaultFormat);
        var source = context.GetService<IRelationalTypeMappingSource>();
        var guid = new Guid("00112233-4455-6677-8899-aabbccddeeff");
        object value = clrType == typeof(Guid) ? guid : "plain text";
        var expected = clrType == typeof(Guid) ? $"'{guid}'" : "'plain text'";

        // Act
        var mapping = source.FindMapping(clrType, storeType);
        var literal = mapping?.GenerateSqlLiteral(value);

        // Assert
        Assert.NotNull(mapping);
        Assert.Equal(clrType, mapping.ClrType);
        Assert.Equal(expected, literal);

        if (clrType == typeof(string))
        {
            Assert.Null(mapping.Converter);
        }
    }

    private static void AssertTextMapping(
        string sql,
        string propertyName,
        MySqlGuidFormat defaultFormat,
        bool canonicalizeText
    )
    {
        var binary = propertyName.Contains("Binary", StringComparison.Ordinal)
            || (propertyName.Contains("Default", StringComparison.Ordinal)
                && defaultFormat == MySqlGuidFormat.Binary16);

        var column = $"`g`.`{propertyName}`";

        if (binary)
        {
            Assert.Contains($"HEX({column})", sql, StringComparison.Ordinal);
            Assert.Contains("LOWER(CONCAT(", sql, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain($"HEX({column})", sql, StringComparison.Ordinal);
            Assert.Contains(
                canonicalizeText ? $"LOWER({column})" : $"{column} LIKE",
                sql,
                StringComparison.Ordinal);
        }
    }

    private static GuidContext CreateContext(
        MySqlGuidFormat defaultFormat
    ) => new(MySqlFunctionalTestOptions
        .CreateTransientBuilder<GuidContext>()
        .UseMySql(
            "Server=localhost;Database=guid_translation;User ID=unused;Password=unused",
            MySqlServerVersion.MySql(new Version(8, 4, 0)),
            options => options.DefaultGuidFormat(defaultFormat))
        .Options);

    private sealed class GuidContext : DbContext
    {
        public GuidContext(
            DbContextOptions<GuidContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var entity = modelBuilder.Entity<GuidItem>();
            entity.ToTable("GuidItems");

            foreach (var prefix in new[] { string.Empty, "Optional" })
            {
                var type = prefix.Length == 0 ? typeof(Guid) : typeof(Guid?);

                entity.Property(type, $"{prefix}DefaultToken");
                entity
                    .Property(type, $"{prefix}BinaryToken")
                    .HasMySqlGuidFormat(MySqlGuidFormat.Binary16);

                entity
                    .Property(type, $"{prefix}TextToken")
                    .HasMySqlGuidFormat(MySqlGuidFormat.Char36);

                entity
                    .Property(type, $"{prefix}BinaryColumnToken")
                    .HasColumnType("binary(16)");

                entity
                    .Property(type, $"{prefix}CharColumnToken")
                    .HasColumnType("char(36)");

                entity
                    .Property(type, $"{prefix}VarCharColumnToken")
                    .HasColumnType("varchar(36)");

                entity
                    .Property(type, $"{prefix}ConvertedTextToken")
                    .HasConversion<Microsoft.EntityFrameworkCore.Storage.ValueConversion.GuidToStringConverter>();
            }

            entity
                .Property<Guid>("CustomTextToken")
                .HasConversion(value => value.ToString("N"), value => Guid.ParseExact(value, "N"))
                .HasColumnType("char(36)");

            entity
                .Property<Guid>("LittleEndianToken")
                .HasConversion<byte[]>();
        }
    }

    private sealed class GuidItem
    {
        public int Id { get; set; }
        public string Pattern { get; set; } = string.Empty;
    }
}
