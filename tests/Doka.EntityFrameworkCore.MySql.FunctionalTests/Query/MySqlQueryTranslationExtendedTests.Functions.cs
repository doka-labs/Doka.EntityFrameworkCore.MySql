namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlQueryTranslationExtendedTests
{
    /// <summary>
    /// EF.Functions.Like with escape character produces LIKE ... ESCAPE.
    /// </summary>
    [Fact]
    public void Ef_functions_like_with_escape_translates_correctly()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.Where(e => EF.Functions.Like(e.Name, "%test%", "\\"))
            .ToQueryString();

        // Assert
        Assert.Contains("LIKE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ESCAPE", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// EF.Functions.Match produces MATCH(...) AGAINST(...) SQL.
    /// </summary>
    [Fact]
    public void Ef_functions_match_produces_match_against_sql()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.Where(e => EF.Functions.Match(e.Name, "search term"))
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "MATCH");

        Assert.Contains("AGAINST", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// EF.Functions.MatchInBooleanMode produces MATCH(...) AGAINST(... IN BOOLEAN MODE) SQL.
    /// </summary>
    [Fact]
    public void Ef_functions_match_boolean_mode_produces_correct_sql()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.Where(e => EF.Functions.MatchInBooleanMode(e.Name, "+required -excluded"))
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "MATCH");

        Assert.Contains("AGAINST", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IN BOOLEAN MODE", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// EF.Functions.Regexp produces REGEXP_LIKE SQL on MySQL 8.0+.
    /// </summary>
    [Fact]
    public void Ef_functions_regexp_produces_regexp_like_on_mysql()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.Where(e => EF.Functions.Regexp(e.Name, "^test.*$"))
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsRegularExpression(sql, mariaDb: false);
    }

    /// <summary>
    /// EF.Functions.Regexp produces infix REGEXP on MariaDB.
    /// </summary>
    [Fact]
    public void Ef_functions_regexp_produces_infix_regexp_on_mariadb()
    {
        // Arrange
        using var context = CreateMariaDbContext();

        // Act
        var sql = context
            .Items.Where(e => EF.Functions.Regexp(e.Name, "^test.*$"))
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsRegularExpression(sql, mariaDb: true);

        Assert.DoesNotContain("REGEXP_LIKE", sql, StringComparison.OrdinalIgnoreCase);
    }
}
