namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Contains the jsondocuments responsibility for MySqlPrimitiveCollectionParameterTests.</summary>
public sealed partial class MySqlPrimitiveCollectionParameterTests
{
    /// <summary>
    /// An explicit context-wide JSON mode remains available for workloads
    /// where its plan and command cost have been measured.
    /// </summary>
    [Fact]
    public void Single_parameter_context_override_uses_json_table()
    {
        // Arrange
        using var context = CreateContext(
            parameterTranslationMode: ParameterTranslationMode.Parameter);

        var values = new[] { s_first, s_second };

        // Act
        var sql = context
            .Items
            .Where(item => values.Contains(item.BinaryId))
            .ToQueryString();

        // Assert
        Assert.Contains("JSON_TABLE", sql, StringComparison.Ordinal);
        Assert.Contains("`value` char(36) PATH '$'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(" IN (@", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// A per-query multiple-parameter marker overrides a JSON context mode,
    /// so callers can recover a cardinality-visible plan without a new context.
    /// </summary>
    [Fact]
    public void Multiple_parameter_query_override_bypasses_json_context_mode()
    {
        // Arrange
        using var context = CreateContext(
            parameterTranslationMode: ParameterTranslationMode.Parameter);

        var values = new[] { s_first, s_second };

        // Act
        var sql = context
            .Items
            .Where(item => EF.MultipleParameters(values).Contains(item.BinaryId))
            .ToQueryString();

        // Assert
        Assert.Contains(" IN (", sql, StringComparison.Ordinal);
        Assert.Contains("@values", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_TABLE", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// An explicit constant mode remains available without being rewritten to
    /// the multiple-parameter default.
    /// </summary>
    [Fact]
    public void Constant_override_bypasses_json_table_and_parameters()
    {
        // Arrange
        using var context = CreateContext(
            parameterTranslationMode: ParameterTranslationMode.Constant);

        var values = new[] { s_first, s_second };

        // Act
        var sql = context
            .Items
            .Where(item => values.Contains(item.CharId))
            .ToQueryString();

        // Assert
        Assert.Contains(" IN (", sql, StringComparison.Ordinal);
        Assert.Contains(s_first.ToString(), sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("JSON_TABLE", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@values", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Deferred byte-array elements must preserve their JSON Base64 decoding.
    /// </summary>
    [Fact]
    public void Byte_array_parameter_collection_decodes_json_text_after_type_inference()
    {
        // Arrange
        using var context = CreateContext();
        byte[][] values = [[1, 2, 3], [4, 5, 6]];

        // Act
        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Contains(item.Payload))
            .ToQueryString();

        // Assert
        Assert.Contains("`value` longtext PATH '$'", sql, StringComparison.Ordinal);
        Assert.Contains("FROM_BASE64(`v`.`value`)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` varbinary(32) PATH '$'", sql, StringComparison.Ordinal);
    }
}
