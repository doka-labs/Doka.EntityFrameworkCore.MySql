namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlQueryTranslationExtendedTests
{
    /// <summary>
    /// String.Substring server translation.
    /// </summary>
    [Fact]
    public void String_substring_translates_to_mysql_substring()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.Select(e => e.Name.Substring(2))
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "SUBSTRING");
    }

    /// <summary>Verifies ToUpper is translated to the server's UPPER function.</summary>
    [Fact]
    public void String_upper_translates_to_mysql_function()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        // WHY: The call is an expression-tree node translated to SQL;
        // the CLR culture is never consulted by UPPER().
#pragma warning disable CA1304, CA1311
        var sql = context
            .Items.Select(item => item.Name.ToUpper())
            .ToQueryString();
#pragma warning restore CA1304, CA1311

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "UPPER");
    }

    /// <summary>Verifies ToLower is translated to the server's LOWER function.</summary>
    [Fact]
    public void String_lower_translates_to_mysql_function()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        // WHY: The call is an expression-tree node translated to SQL;
        // the CLR culture is never consulted by LOWER().
#pragma warning disable CA1304, CA1311
        var sql = context
            .Items.Select(item => item.Name.ToLower())
            .ToQueryString();
#pragma warning restore CA1304, CA1311

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "LOWER");
    }

    /// <summary>
    /// String.Replace server translation.
    /// </summary>
    [Fact]
    public void String_replace_translates_to_mysql_replace()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.Select(e => e.Name.Replace("old", "new"))
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "REPLACE");
    }

    /// <summary>
    /// String.Trim server translation.
    /// </summary>
    [Fact]
    public void String_trim_translates_to_mysql_trim()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.Select(e => e.Name.Trim())
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "TRIM");
    }

    /// <summary>
    /// String.IndexOf server translation.
    /// </summary>
    [Fact]
    public void String_indexof_translates_to_locate_minus_one()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.Select(e => e.Name.IndexOf("test"))
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "LOCATE");
    }

    /// <summary>
    /// String.Concat server translation.
    /// </summary>
    [Fact]
    public void String_concat_translates_to_mysql_concat()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.Select(e => string.Concat(e.Name, "-suffix"))
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "CONCAT");
    }

    /// <summary>
    /// Numeric conversion of characters uses CLR character values rather than the
    /// database's decimal parsing of character text.
    /// </summary>
    [Fact]
    public void String_character_join_uses_clr_character_values()
    {
        // Arrange
        using var context = CreateContext();
        var characters = "12";

        // Act
        var sql = context
            .Items.Join(
                characters,
                item => item.Id,
                character => character,
                (item, _) => item.Id)
            .ToQueryString();

        // Assert
        Assert.Contains(
            "CONV(HEX(CONVERT(",
            sql,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("USING utf32", sql, StringComparison.OrdinalIgnoreCase);
    }
}
