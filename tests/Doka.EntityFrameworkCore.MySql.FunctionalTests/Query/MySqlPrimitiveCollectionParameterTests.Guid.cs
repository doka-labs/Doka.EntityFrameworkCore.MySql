namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Contains the guid responsibility for MySqlPrimitiveCollectionParameterTests.</summary>
public sealed partial class MySqlPrimitiveCollectionParameterTests
{
    /// <summary>
    /// Ordinary Binary16 membership retains EF Core's multiple-parameter
    /// default instead of entering the JSON_TABLE translation path.
    /// </summary>
    [Fact]
    public void Ordinary_binary_guid_collection_uses_multiple_parameters_by_default()
    {
        // Arrange
        using var context = CreateContext();
        var values = new[] { s_first, s_second };

        // Act
        var sql = context
            .Items
            .Where(item => values.Contains(item.BinaryId))
            .ToQueryString();

        // Assert
        Assert.Contains(" IN (", sql, StringComparison.Ordinal);
        Assert.Contains("@values", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_TABLE", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ordinary Char36 membership retains EF Core's multiple-parameter
    /// default instead of entering the JSON_TABLE translation path.
    /// </summary>
    [Fact]
    public void Ordinary_char_guid_collection_uses_multiple_parameters_by_default()
    {
        // Arrange
        using var context = CreateContext();
        var values = new[] { s_first, s_second };

        // Act
        var sql = context
            .Items
            .Where(item => values.Contains(item.CharId))
            .ToQueryString();

        // Assert
        Assert.Contains(" IN (", sql, StringComparison.Ordinal);
        Assert.Contains("@values", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_TABLE", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// A deferred Binary16 element mapping must decode the JSON GUID text before comparison.
    /// </summary>
    [Fact]
    public void Binary_guid_parameter_collection_decodes_json_text_after_type_inference()
    {
        // Arrange
        using var context = CreateContext();
        var values = new[] { s_first, s_second };

        // Act
        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Contains(item.BinaryId))
            .ToQueryString();

        // Assert
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
        // Arrange
        using var context = CreateContext();
        var values = new[] { s_first, s_second };

        // Act
        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Contains(item.CharId))
            .ToQueryString();

        // Assert
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
        // Arrange
        using var context = CreateContext();
        Guid?[] values = [s_first, null];

        // Act
        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Contains(item.OptionalBinaryId))
            .ToQueryString();

        // Assert
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
        // Arrange
        using var context = CreateContext();
        Guid?[] values = [s_first, null];

        // Act
        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Contains(item.OptionalCharId))
            .ToQueryString();

        // Assert
        Assert.Contains("`value` char(36) PATH '$'", sql, StringComparison.Ordinal);
        Assert.Contains("JSON_UNQUOTE(JSON_QUOTE(`v`.`value`))", sql, StringComparison.Ordinal);
        Assert.Contains("IS NULL", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("UNHEX(", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// A model-to-Guid converter retains Binary16 decoding at the JSON
    /// transport boundary.
    /// </summary>
    [Fact]
    public void Value_object_to_guid_parameter_collection_uses_provider_representation()
    {
        // Arrange
        using var context = CreateContext();
        StrongGuid[] values = [new(s_first)];

        // Act
        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Contains(item.ConvertedGuid))
            .ToQueryString();

        // Assert
        Assert.Contains("`value` char(36) PATH '$'", sql, StringComparison.Ordinal);
        Assert.Contains("UNHEX(REPLACE(`v`.`value`, '-', ''))", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` binary(16) PATH '$'", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// A model-mapped GUID collection retains the same decoding when its
    /// element mapping is known during initial translation.
    /// </summary>
    [Fact]
    public void Model_guid_collection_decodes_json_text_with_early_mapping()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items
            .Where(item => item.GuidValues.Contains(s_first))
            .ToQueryString();

        // Assert
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
        // Arrange
        using var context = CreateContext(MySqlGuidFormat.Char36);

        // Act
        var sql = context
            .Items
            .Where(item => item.GuidValues.Contains(s_first))
            .ToQueryString();

        // Assert
        Assert.Contains("`value` char(36) PATH '$'", sql, StringComparison.Ordinal);
        Assert.Contains("JSON_UNQUOTE(JSON_QUOTE(`g`.`value`))", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("UNHEX(", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` binary(16) PATH '$'", sql, StringComparison.Ordinal);
    }
}
