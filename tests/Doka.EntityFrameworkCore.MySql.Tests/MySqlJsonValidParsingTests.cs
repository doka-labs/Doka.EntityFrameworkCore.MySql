namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Tests <c>ExtractJsonValidColumnName</c> parsing logic in <see cref="MySqlDatabaseModelFactory"/>
/// via reflection (the method is private static).
/// </summary>
public sealed class MySqlJsonValidParsingTests
{
    /// <summary>Standard backtick-delimited column reference.</summary>
    [Fact]
    public void Extracts_backtick_delimited_column() => Assert.Equal("Data", Extract("json_valid(`Data`)"));

    /// <summary>Unquoted column reference.</summary>
    [Fact]
    public void Extracts_unquoted_column() => Assert.Equal("payload", Extract("json_valid(payload)"));

    /// <summary>Mixed case function name is handled.</summary>
    [Fact]
    public void Handles_mixed_case_function_name() => Assert.Equal("Col", Extract("JSON_VALID(`Col`)"));

    /// <summary>Extra whitespace around column reference.</summary>
    [Fact]
    public void Handles_whitespace_around_column() => Assert.Equal("Info", Extract("json_valid(  `Info`  )"));

    /// <summary>No json_valid in the clause returns null.</summary>
    [Fact]
    public void Returns_null_for_unrelated_check_clause() => Assert.Null(Extract("CHECK (LENGTH(col) > 0)"));

    /// <summary>Empty parentheses returns null.</summary>
    [Fact]
    public void Returns_null_for_empty_parentheses() => Assert.Null(Extract("json_valid()"));

    /// <summary>Only whitespace inside parentheses returns null.</summary>
    [Fact]
    public void Returns_null_for_whitespace_only_column() => Assert.Null(Extract("json_valid(   )"));

    /// <summary>Nested expression with json_valid extracts the inner column.</summary>
    [Fact]
    public void Handles_nested_expression() => Assert.Equal(
        "metadata",
        Extract("(`metadata` is null or json_valid(`metadata`))"));

    private static string? Extract(
        string checkClause
    )
    {
        var method = typeof(MySqlDatabaseModelFactory).GetMethod(
            "ExtractJsonValidColumnName",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        return (string?)method!.Invoke(null, [checkClause]);
    }
}
