namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Pins GUID text translation to the actual storage mapping, independently of
/// the context default and the property's CLR type.
/// </summary>
public sealed class MySqlGuidTextTranslationTests
{
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

    [Theory]
    [InlineData(MySqlGuidFormat.Binary16)]
    [InlineData(MySqlGuidFormat.Char36)]
    public void ToString_formats_each_supported_mapping_as_canonical_text(
        MySqlGuidFormat defaultFormat
    )
    {
        using var context = CreateContext(defaultFormat);

        foreach (var propertyName in s_properties)
        {
            var sql = context
                .Set<GuidItem>()
                .Where(item => EF
                    .Property<Guid>(item, propertyName)
                    .ToString()
                    == "00112233-4455-6677-8899-aabbccddeeff")
                .ToQueryString();

            AssertTextMapping(sql, propertyName, defaultFormat, canonicalizeText: true);
        }
    }

    [Theory]
    [InlineData(MySqlGuidFormat.Binary16)]
    [InlineData(MySqlGuidFormat.Char36)]
    public void Like_formats_binary_mappings_and_preserves_text_collation(
        MySqlGuidFormat defaultFormat
    )
    {
        using var context = CreateContext(defaultFormat);

        foreach (var propertyName in s_properties)
        {
            var sql = context
                .Set<GuidItem>()
                .Where(item => EF.Functions.Like(EF.Property<Guid>(item, propertyName), "00112233-%"))
                .ToQueryString();

            AssertTextMapping(sql, propertyName, defaultFormat, canonicalizeText: false);

            var optionalName = $"Optional{propertyName}";
            var optionalSql = context
                .Set<GuidItem>()
                .Where(item => EF.Functions.Like(EF.Property<Guid?>(item, optionalName), "00112233-%", "!"))
                .ToQueryString();

            AssertTextMapping(optionalSql, optionalName, defaultFormat, canonicalizeText: false);
            Assert.Contains("ESCAPE '!'", optionalSql, StringComparison.Ordinal);
            Assert.DoesNotContain("COALESCE(", optionalSql, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(MySqlGuidFormat.Binary16)]
    [InlineData(MySqlGuidFormat.Char36)]
    public void NewGuid_uses_the_context_default_storage_representation(
        MySqlGuidFormat defaultFormat
    )
    {
        using var context = CreateContext(defaultFormat);
        var mapping = context
            .GetService<IRelationalTypeMappingSource>()
            .FindMapping(typeof(Guid));

        Assert.NotNull(mapping);
        Assert.Equal(typeof(Guid), mapping.ClrType);

        var query = context
            .Set<GuidItem>()
            .Select(item => Guid.NewGuid());

        var sql = query.ToQueryString();

        Assert.Contains("UUID()", sql, StringComparison.Ordinal);

        if (defaultFormat == MySqlGuidFormat.Binary16)
        {
            Assert.Contains("UNHEX(REPLACE(UUID()", sql, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("UNHEX(", sql, StringComparison.Ordinal);
        }

        var formattedSql = context
            .Set<GuidItem>()
            .Select(item => Guid
                .NewGuid()
                .ToString())
            .ToQueryString();

        Assert.Contains("UUID()", formattedSql, StringComparison.Ordinal);
        Assert.Equal(
            defaultFormat == MySqlGuidFormat.Binary16,
            formattedSql.Contains("HEX(", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(MySqlGuidFormat.Binary16, "CustomTextToken")]
    [InlineData(MySqlGuidFormat.Char36, "CustomTextToken")]
    [InlineData(MySqlGuidFormat.Binary16, "LittleEndianToken")]
    [InlineData(MySqlGuidFormat.Char36, "LittleEndianToken")]
    public void Guid_text_translation_rejects_unknown_or_incompatible_converter_layouts(
        MySqlGuidFormat defaultFormat,
        string propertyName
    )
    {
        using var context = CreateContext(defaultFormat);

        var toStringException = Assert.Throws<InvalidOperationException>(() => context
            .Set<GuidItem>()
            .Where(item => EF
                .Property<Guid>(item, propertyName)
                .ToString() == "00112233-4455-6677-8899-aabbccddeeff")
            .ToQueryString());

        var likeException = Assert.Throws<InvalidOperationException>(() => context
            .Set<GuidItem>()
            .Where(item => EF.Functions.Like(EF.Property<Guid>(item, propertyName), "00112233-%"))
            .ToQueryString());

        Assert.Contains("GUID text translation does not support", toStringException.Message, StringComparison.Ordinal);
        Assert.Contains("GUID text translation does not support", likeException.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(MySqlGuidFormat.Binary16)]
    [InlineData(MySqlGuidFormat.Char36)]
    public void Guid_parameters_use_the_context_default_without_converting_the_pattern(
        MySqlGuidFormat defaultFormat
    )
    {
        using var context = CreateContext(defaultFormat);
        Guid? token = new Guid("00112233-4455-6677-8899-aabbccddeeff");
        var sql = context
            .Set<GuidItem>()
            .Where(item => EF.Functions.Like(token, item.Pattern))
            .ToQueryString();

        Assert.Contains("LIKE `g`.`Pattern`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("HEX(`g`.`Pattern`)", sql, StringComparison.Ordinal);
        Assert.Equal(
            defaultFormat == MySqlGuidFormat.Binary16,
            sql.Contains("HEX(@token)", StringComparison.Ordinal));

        var constantSql = context
            .Set<GuidItem>()
            .Where(item => EF.Functions.Like(new Guid("00112233-4455-6677-8899-aabbccddeeff"), item.Pattern))
            .ToQueryString();

        Assert.Contains("LIKE `g`.`Pattern`", constantSql, StringComparison.Ordinal);
        Assert.Contains(
            defaultFormat == MySqlGuidFormat.Binary16
                ? "HEX(X'00112233445566778899AABBCCDDEEFF')"
                : "'00112233-4455-6677-8899-aabbccddeeff' LIKE",
            constantSql,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(MySqlGuidFormat.Binary16)]
    [InlineData(MySqlGuidFormat.Char36)]
    public void Text_store_type_lookups_keep_guid_and_string_CLR_contracts_separate(
        MySqlGuidFormat defaultFormat
    )
    {
        using var context = CreateContext(defaultFormat);
        var source = context.GetService<IRelationalTypeMappingSource>();
        var guid = new Guid("00112233-4455-6677-8899-aabbccddeeff");

        foreach (var storeType in new[] { "char(36)", "varchar(36)" })
        {
            var guidMapping = source.FindMapping(typeof(Guid), storeType);
            var stringMapping = source.FindMapping(typeof(string), storeType);

            Assert.NotNull(guidMapping);
            Assert.NotNull(stringMapping);
            Assert.Equal(typeof(Guid), guidMapping.ClrType);
            Assert.Equal(typeof(string), stringMapping.ClrType);
            Assert.Null(stringMapping.Converter);
            Assert.Equal($"'{guid}'", guidMapping.GenerateSqlLiteral(guid));
            Assert.Equal("'plain text'", stringMapping.GenerateSqlLiteral("plain text"));
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
