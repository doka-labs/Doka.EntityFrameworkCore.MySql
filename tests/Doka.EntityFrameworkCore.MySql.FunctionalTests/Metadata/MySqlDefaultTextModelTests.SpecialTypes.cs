namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Verifies opted-in defaults alongside actual GUID, text, converter, binary and spatial mappings.</summary>
public sealed partial class MySqlDefaultTextModelTests
{
    /// <summary>All supported GUID formats retain their storage and transport contracts under text defaults.</summary>
    /// <param name="mariaDb">Whether to use the MariaDB profile.</param>
    /// <param name="format">The configured default GUID representation.</param>
    /// <param name="storeType">The expected physical GUID store type.</param>
    [Theory]
    [InlineData(false, MySqlGuidFormat.Binary16, "binary(16)")]
    [InlineData(false, MySqlGuidFormat.Char36, "char(36)")]
    [InlineData(true, MySqlGuidFormat.Binary16, "binary(16)")]
    [InlineData(true, MySqlGuidFormat.Char36, "char(36)")]
    [InlineData(true, MySqlGuidFormat.Uuid, "uuid")]
    public void Enabled_text_defaults_preserve_guid_store_and_transport_formats(
        bool mariaDb,
        MySqlGuidFormat format,
        string storeType
    )
    {
        // Arrange
        using var context = new GuidDefaultsContext(SpecialTypeOptions<GuidDefaultsContext>(mariaDb, format));
        var value = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        using var command = new MySqlCommand();

        // Act
        var property =
            context.Model.FindEntityType(typeof(GuidDefaultsRow))!.FindProperty(nameof(GuidDefaultsRow.Value))!;

        var mapping = property.GetRelationalTypeMapping();
        var literal = mapping.GenerateSqlLiteral(value);
        var parameter = mapping.CreateParameter(command, "value", value);
        var column = ColumnDefinition(context.Database.GenerateCreateScript(), nameof(GuidDefaultsRow.Value));

        // Assert
        Assert.Equal(typeof(Guid), property.ClrType);
        Assert.Equal(format, property.GetMySqlGuidFormat());
        Assert.Equal(storeType, property.GetColumnType());
        Assert.Contains("`Value` " + storeType, column, StringComparison.OrdinalIgnoreCase);
        if (format == MySqlGuidFormat.Binary16)
        {
            Assert.Equal(value, parameter.Value);
            Assert.Equal("X'00112233445566778899AABBCCDDEEFF'", literal);
        }
        else
        {
            Assert.Equal(value.ToString("D"), parameter.Value);
            Assert.Equal("'00112233-4455-6677-8899-aabbccddeeff'", literal);
        }

        if (format != MySqlGuidFormat.Char36)
        {
            Assert.DoesNotContain("COLLATE", column, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("CHARACTER SET", column, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Text defaults do not turn MySQL into a native UUID-capable engine.</summary>
    [Fact]
    public void Enabled_text_defaults_do_not_bypass_mysql_native_uuid_rejection()
    {
        // Arrange
        static void act()
        {
            using var context = new GuidDefaultsContext(
                SpecialTypeOptions<GuidDefaultsContext>(mariaDb: false, MySqlGuidFormat.Uuid));

            _ = context.Model;
        }

        // Act
        var exception = Record.Exception(act);

        // Assert
        Assert.IsType<NotSupportedException>(exception);
    }

    /// <summary>Explicit ASCII wins while a non-Unicode hint alone keeps its established text-family behavior.</summary>
    /// <param name="mariaDb">Whether to use the MariaDB profile.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Enabled_text_defaults_preserve_ascii_override_and_non_unicode_hint(
        bool mariaDb
    )
    {
        // Arrange
        using var context = new ScalarDefaultsContext(SpecialTypeOptions<ScalarDefaultsContext>(mariaDb));

        // Act
        var model = context.GetService<IDesignTimeModel>()
            .Model;

        var entity = model.FindEntityType(typeof(ScalarDefaultsRow))!;
        var ascii = entity.FindProperty(nameof(ScalarDefaultsRow.AsciiText))!;
        var hinted = entity.FindProperty(nameof(ScalarDefaultsRow.HintedText))!;
        var script = context.Database.GenerateCreateScript();
        var asciiColumn = ColumnDefinition(script, nameof(ScalarDefaultsRow.AsciiText));
        var hintedColumn = ColumnDefinition(script, nameof(ScalarDefaultsRow.HintedText));

        // Assert
        Assert.Equal("utf8mb4_unicode_ci", model.GetCollation());
        Assert.False(ascii.IsUnicode());
        Assert.Equal("ascii_general_ci", ascii.GetCollation());
        Assert.Contains("ascii_general_ci", asciiColumn, StringComparison.Ordinal);
        Assert.DoesNotContain("utf8mb4_unicode_ci", asciiColumn, StringComparison.Ordinal);
        Assert.False(hinted.IsUnicode());
        Assert.Equal("varchar(32)", hinted.GetColumnType());
        Assert.Null(hinted.FindAnnotation(MySqlAnnotationNames.CharSet));
        Assert.Null(hinted.GetCollation());
        Assert.DoesNotContain("ascii", hintedColumn, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An application GUID converter remains authoritative rather than being normalized to Binary16.</summary>
    /// <param name="mariaDb">Whether to use the MariaDB profile.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Enabled_text_defaults_preserve_application_value_converter(
        bool mariaDb
    )
    {
        // Arrange
        using var context = new ScalarDefaultsContext(SpecialTypeOptions<ScalarDefaultsContext>(mariaDb));
        var value = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        using var command = new MySqlCommand();

        // Act
        var property = context.Model.FindEntityType(typeof(ScalarDefaultsRow))!
            .FindProperty(nameof(ScalarDefaultsRow.ConvertedGuid))!;

        var mapping = property.GetRelationalTypeMapping();
        var parameter = mapping.CreateParameter(command, "converted", value);
        var literal = mapping.GenerateSqlLiteral(value);
        var column = ColumnDefinition(context.Database.GenerateCreateScript(), nameof(ScalarDefaultsRow.ConvertedGuid));

        // Assert
        Assert.IsType<GuidToStringConverter>(property.GetValueConverter());
        Assert.Equal("varchar(36)", property.GetColumnType());
        Assert.Equal(value.ToString("D"), parameter.Value);
        Assert.Equal("'00112233-4455-6677-8899-aabbccddeeff'", literal);
        Assert.Contains("varchar(36)", column, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("binary(16)", column, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Binary and BLOB columns keep exact byte mappings and receive no textual DDL facets.</summary>
    /// <param name="mariaDb">Whether to use the MariaDB profile.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Enabled_text_defaults_preserve_binary_and_blob_columns(
        bool mariaDb
    )
    {
        // Arrange
        using var context = new ScalarDefaultsContext(SpecialTypeOptions<ScalarDefaultsContext>(mariaDb));
        byte[] value = [0x00, 0xff, 0x27, 0x22];

        // Act
        var entity = context.Model.FindEntityType(typeof(ScalarDefaultsRow))!;
        var mapping = entity.FindProperty(nameof(ScalarDefaultsRow.Bytes))!.GetRelationalTypeMapping();
        var literal = mapping.GenerateSqlLiteral(value);
        var script = context.Database.GenerateCreateScript();
        var bytesColumn = ColumnDefinition(script, nameof(ScalarDefaultsRow.Bytes));
        var blobColumn = ColumnDefinition(script, nameof(ScalarDefaultsRow.Blob));

        // Assert
        Assert.Equal("varbinary(32)", mapping.StoreType);
        Assert.Equal("X'00FF2722'", literal);
        Assert.Contains("varbinary(32)", bytesColumn, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("longblob", blobColumn, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("COLLATE", bytesColumn, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CHARACTER SET", bytesColumn, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("COLLATE", blobColumn, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CHARACTER SET", blobColumn, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Spatial mappings and translation remain native when text defaults and NTS are both enabled.</summary>
    /// <param name="mariaDb">Whether to use the MariaDB profile.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Enabled_text_defaults_preserve_spatial_mapping_and_query_translation(
        bool mariaDb
    )
    {
        // Arrange
        using var context = new ScalarDefaultsContext(SpecialTypeOptions<ScalarDefaultsContext>(mariaDb));
        var query = context
            .Set<ScalarDefaultsRow>()
            .Select(row => row.Location.X);

        // Act
        var property = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(ScalarDefaultsRow))!
            .FindProperty(nameof(ScalarDefaultsRow.Location))!;

        var sql = query.ToQueryString();
        var column = ColumnDefinition(context.Database.GenerateCreateScript(), nameof(ScalarDefaultsRow.Location));

        // Assert
        Assert.Equal("point", property.GetColumnType());
        Assert.Equal(4326, property.FindAnnotation(MySqlAnnotationNames.SpatialReferenceSystemId)!.Value);
        Assert.Contains("ST_X(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("point", column, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("COLLATE", column, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CHARACTER SET", column, StringComparison.OrdinalIgnoreCase);
    }
}
