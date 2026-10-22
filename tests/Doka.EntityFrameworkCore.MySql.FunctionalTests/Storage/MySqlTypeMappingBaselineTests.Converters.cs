using System.Data;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Contains the converters responsibility for MySqlTypeMappingBaselineTests.</summary>
public sealed partial class MySqlTypeMappingBaselineTests
{
    /// <summary>
    /// Verifies that BoolToStringConverter produces a varchar column, not tinyint(1).
    /// Validates ValueConverter provider type propagation.
    /// </summary>
    [Fact]
    public void Bool_to_string_converter_produces_varchar_column()
    {
        // Arrange
        using var context = new ValueConverterContext(CreateOptions<ValueConverterContext>());

        // Act
        var entityType = context.Model.FindEntityType(typeof(ValueConverterEntity))!;

        // Assert
        Assert.Equal("varchar(1)", entityType.FindProperty(nameof(ValueConverterEntity.IsActiveText))!.GetColumnType());
    }

    /// <summary>
    /// Verifies that explicit LOB store types override converter size hints in both
    /// annotation metadata and the resolved relational type mapping.
    /// </summary>
    [Theory]
    [InlineData(nameof(ValueConverterEntity.ExplicitText), "longtext")]
    [InlineData(nameof(ValueConverterEntity.ExplicitBlob), "longblob")]
    public void Explicit_lob_store_types_override_converter_size_hints(
        string propertyName,
        string expected
    )
    {
        // Arrange
        using var context = new ValueConverterContext(CreateOptions<ValueConverterContext>());

        // Act
        var entityType = context.Model.FindEntityType(typeof(ValueConverterEntity))!;
        var property = entityType.FindProperty(propertyName)!;

        // Assert
        Assert.Equal(expected, property.GetColumnType());
        Assert.Equal(expected, property.GetRelationalTypeMapping().StoreType);
    }

    /// <summary>
    /// Verifies that a custom domain type converter (Money -> decimal) produces decimal column.
    /// </summary>
    [Fact]
    public void Custom_domain_type_converter_produces_correct_column_type()
    {
        // Arrange
        using var context = new ValueConverterContext(CreateOptions<ValueConverterContext>());

        // Act
        var entityType = context.Model.FindEntityType(typeof(ValueConverterEntity))!;

        // Assert
        Assert.Equal("decimal(18,2)", entityType.FindProperty(nameof(ValueConverterEntity.Price))!.GetColumnType());
    }

    /// <summary>
    /// Verifies that Uri property with UriToStringConverter produces a text column.
    /// </summary>
    [Fact]
    public void Uri_to_string_converter_produces_text_column()
    {
        // Arrange
        using var context = new ValueConverterContext(CreateOptions<ValueConverterContext>());

        // Act
        var entityType = context.Model.FindEntityType(typeof(ValueConverterEntity))!;
        var columnType = entityType.FindProperty(nameof(ValueConverterEntity.Website))!.GetColumnType();

        // Assert
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
        // Arrange
        using var context = new ValueConverterContext(CreateOptions<ValueConverterContext>());

        // Act
        var entityType = context.Model.FindEntityType(typeof(ValueConverterEntity))!;
        var columnType = entityType.FindProperty(nameof(ValueConverterEntity.IsVerifiedInt))!.GetColumnType();

        // Assert
        Assert.Equal("int", columnType);
    }

    /// <summary>
    /// Converter in OrderBy produces correct SQL.
    /// </summary>
    [Fact]
    public void Converter_in_orderby_produces_valid_sql()
    {
        // Arrange
        using var context = new ValueConverterContext(CreateOptions<ValueConverterContext>());

        // Act
        var sql = context
            .Set<ValueConverterEntity>()
            .OrderBy(e => e.Price)
            .ToQueryString();

        // Assert
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
    }
}
