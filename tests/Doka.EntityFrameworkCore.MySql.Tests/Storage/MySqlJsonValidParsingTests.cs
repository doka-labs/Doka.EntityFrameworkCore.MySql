namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Pins the JSON_VALID column-name parser surfaced via
/// <see cref="ScaffoldingHelpers.ExtractJsonValidColumnName"/>. The helper is used by
/// the MariaDB JSON-check-constraint loader to lift LONGTEXT columns guarded by a
/// json_valid CHECK back to the canonical "json" store type.
/// </summary>
public sealed class MySqlJsonValidParsingTests
{
    /// <summary>
    /// Verifies extracts backtick delimited column.
    /// </summary>
    [Fact]
    public void Extracts_backtick_delimited_column()
    {
        // Arrange
        const string input = "json_valid(`Data`)";

        // Act
        var actual = ScaffoldingHelpers.ExtractJsonValidColumnName(input);

        // Assert
        Assert.Equal(
        "Data",
        actual);
    }

    /// <summary>
    /// Verifies extracts unquoted column.
    /// </summary>
    [Fact]
    public void Extracts_unquoted_column()
    {
        // Arrange
        const string input = "json_valid(payload)";

        // Act
        var actual = ScaffoldingHelpers.ExtractJsonValidColumnName(input);

        // Assert
        Assert.Equal(
        "payload",
        actual);
    }

    /// <summary>
    /// Verifies handles mixed case function name.
    /// </summary>
    [Fact]
    public void Handles_mixed_case_function_name()
    {
        // Arrange
        const string input = "JSON_VALID(`Col`)";

        // Act
        var actual = ScaffoldingHelpers.ExtractJsonValidColumnName(input);

        // Assert
        Assert.Equal(
        "Col",
        actual);
    }

    /// <summary>
    /// Verifies handles whitespace around column.
    /// </summary>
    [Fact]
    public void Handles_whitespace_around_column()
    {
        // Arrange
        const string input = "json_valid(  `Info`  )";

        // Act
        var actual = ScaffoldingHelpers.ExtractJsonValidColumnName(input);

        // Assert
        Assert.Equal(
        "Info",
        actual);
    }

    /// <summary>
    /// Verifies returns null for unrelated check clause.
    /// </summary>
    [Fact]
    public void Returns_null_for_unrelated_check_clause()
    {
        // Arrange
        const string input = "CHECK (LENGTH(col) > 0)";

        // Act
        var actual = ScaffoldingHelpers.ExtractJsonValidColumnName(input);

        // Assert
        Assert.Null(actual);
    }

    /// <summary>
    /// Verifies returns null for empty parentheses.
    /// </summary>
    [Fact]
    public void Returns_null_for_empty_parentheses()
    {
        // Arrange
        const string input = "json_valid()";

        // Act
        var actual = ScaffoldingHelpers.ExtractJsonValidColumnName(input);

        // Assert
        Assert.Null(actual);
    }

    /// <summary>
    /// Verifies returns null for whitespace only column.
    /// </summary>
    [Fact]
    public void Returns_null_for_whitespace_only_column()
    {
        // Arrange
        const string input = "json_valid(   )";

        // Act
        var actual = ScaffoldingHelpers.ExtractJsonValidColumnName(input);

        // Assert
        Assert.Null(actual);
    }

    /// <summary>
    /// Verifies rejects nested expression.
    /// </summary>
    [Fact]
    public void Rejects_nested_expression()
    {
        // Arrange
        const string input = "(`metadata` is null or json_valid(`metadata`))";

        // Act
        var actual = ScaffoldingHelpers.ExtractJsonValidColumnName(input);

        // Assert
        Assert.Null(
        actual);
    }

    /// <summary>
    /// Verifies rejects noncanonical expression.
    /// </summary>
    [Theory]
    [InlineData("json_valid(`metadata`) AND `metadata` IS NOT NULL")]
    [InlineData("NOT json_valid(`metadata`)")]
    [InlineData("json_valid(`metadata`.`value`)")]
    public void Rejects_noncanonical_expression(string checkClause)
    {
        // Arrange: The theory supplies checkClause.

        // Act
        var actual = ScaffoldingHelpers.ExtractJsonValidColumnName(checkClause);

        // Assert
        Assert.Null(
        actual);
    }
}
