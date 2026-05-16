namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Pins the JSON_VALID column-name parser surfaced via
/// <see cref="ScaffoldingHelpers.ExtractJsonValidColumnName"/>. The helper is used by
/// the MariaDB JSON-check-constraint loader to lift LONGTEXT columns guarded by a
/// json_valid CHECK back to the canonical "json" store type.
/// </summary>
public sealed class MySqlJsonValidParsingTests
{
    [Fact]
    public void Extracts_backtick_delimited_column() => Assert.Equal(
        "Data",
        ScaffoldingHelpers.ExtractJsonValidColumnName("json_valid(`Data`)"));

    [Fact]
    public void Extracts_unquoted_column() => Assert.Equal(
        "payload",
        ScaffoldingHelpers.ExtractJsonValidColumnName("json_valid(payload)"));

    [Fact]
    public void Handles_mixed_case_function_name() => Assert.Equal(
        "Col",
        ScaffoldingHelpers.ExtractJsonValidColumnName("JSON_VALID(`Col`)"));

    [Fact]
    public void Handles_whitespace_around_column() => Assert.Equal(
        "Info",
        ScaffoldingHelpers.ExtractJsonValidColumnName("json_valid(  `Info`  )"));

    [Fact]
    public void Returns_null_for_unrelated_check_clause() =>
        Assert.Null(ScaffoldingHelpers.ExtractJsonValidColumnName("CHECK (LENGTH(col) > 0)"));

    [Fact]
    public void Returns_null_for_empty_parentheses() =>
        Assert.Null(ScaffoldingHelpers.ExtractJsonValidColumnName("json_valid()"));

    [Fact]
    public void Returns_null_for_whitespace_only_column() =>
        Assert.Null(ScaffoldingHelpers.ExtractJsonValidColumnName("json_valid(   )"));

    [Fact]
    public void Handles_nested_expression() => Assert.Equal(
        "metadata",
        ScaffoldingHelpers.ExtractJsonValidColumnName("(`metadata` is null or json_valid(`metadata`))"));
}
