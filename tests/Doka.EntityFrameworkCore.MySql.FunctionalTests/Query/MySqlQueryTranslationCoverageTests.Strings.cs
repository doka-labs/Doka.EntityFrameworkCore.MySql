namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Shares the strings responsibility without changing the test fixture.</summary>
public sealed partial class MySqlQueryTranslationCoverageTests
{
    // -- String Method Translations --

    [Fact]
    public void String_TrimStart_translates_to_ltrim()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => e.Name.TrimStart())
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "LTRIM");
    }

    [Fact]
    public void String_TrimEnd_translates_to_rtrim()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => e.Name.TrimEnd())
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "RTRIM");
    }

    [Fact]
    public void String_PadLeft_translates_to_lpad()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => e.Name.PadLeft(10, '0'))
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "LPAD");
    }

    [Fact]
    public void String_PadRight_translates_to_rpad()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => e.Name.PadRight(10, '_'))
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "RPAD");
    }

    [Fact]
    public void String_Substring_two_args_translates_to_mysql_substring()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => e.Name.Substring(2, 5))
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "SUBSTRING");
    }

    [Fact]
    public void String_Concat_three_args_translates_to_concat()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => string.Concat(e.Name, " ", e.Name))
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "CONCAT");
    }

    [Fact]
    public void String_Equals_instance_translates_server_side()
    {
        // Arrange
        using var context = CreateContext();

        // string.Equals inside an IQueryable expression -- EF translates it to a
        // server-side equality comparison; CLR StringComparison is not consulted.
#pragma warning disable CA1309

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Where(e => e.Name.Equals("test"))
            .ToQueryString();

#pragma warning restore CA1309

        // Assert
        Assert.Contains("`Name`", sql, StringComparison.Ordinal);
        Assert.Contains("=", sql, StringComparison.Ordinal);
    }

    // -- GROUP_CONCAT --

    [Fact]
    public void String_Join_translates_to_group_concat()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .GroupBy(e => e.Category)
            .Select(g => new
            {
                Category = g.Key,
                Names = string.Join(", ", g.Select(e => e.Name))
            })
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "GROUP_CONCAT");

        Assert.Contains("SEPARATOR", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GROUP_CONCAT(`c`.`Name`, ", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that multiple aggregate orderings retain their declared order
    /// inside MySQL's GROUP_CONCAT grammar.
    /// </summary>
    [Fact]
    public void String_Join_with_multiple_orderings_translates_to_group_concat()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .GroupBy(e => e.Category)
            .Select(g => string.Join(
                ", ",
                g
                    .OrderBy(e => e.Name)
                    .ThenByDescending(e => e.Id)
                    .Select(e => e.Name)))
            .ToQueryString();

        // Assert
        Assert.Contains("ORDER BY `c`.`Name` ASC, `c`.`Id` DESC", sql, StringComparison.OrdinalIgnoreCase);
    }
}
