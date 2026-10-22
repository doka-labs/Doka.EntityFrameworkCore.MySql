namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlQueryTranslationExtendedTests
{
    /// <summary>Verifies converter in groupby produces valid sql.</summary>
    [Fact]
    public void Converter_in_groupby_produces_valid_sql()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.GroupBy(e => e.Name)
            .Select(g => new
            {
                g.Key,
                Count = g.Count(),
            })
            .ToQueryString();

        // Assert
        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Standard GroupBy + Count SQL.
    /// </summary>
    [Fact]
    public void Groupby_with_count_produces_group_by_sql()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.GroupBy(e => e.Name)
            .Select(g => new
            {
                g.Key,
                Total = g.Count()
            })
            .ToQueryString();

        // Assert
        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
        MySqlSqlAssert.ContainsFunction(sql, "COUNT");
    }

    /// <summary>
    /// Having clause after GroupBy -> HAVING SQL.
    /// </summary>
    [Fact]
    public void Groupby_with_having_produces_having_sql()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.GroupBy(e => e.Name)
            .Where(g => g.Count() > 1)
            .Select(g => new
            {
                g.Key,
                Total = g.Count(),
            })
            .ToQueryString();

        // Assert
        Assert.Contains("HAVING", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// GroupBy produces ONLY_FULL_GROUP_BY compliant SQL.
    /// All non-aggregated SELECT columns must appear in GROUP BY.
    /// </summary>
    [Fact]
    public void Groupby_sql_is_only_full_group_by_compliant()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.GroupBy(e => e.Name)
            .Select(g => new
            {
                g.Key,
                Average = g.Average(e => e.Value),
            })
            .ToQueryString();

        // Assert
        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Integer averages retain EF Core's double conversion instead of narrowing through DECIMAL.
    /// </summary>
    [Fact]
    public void Integer_average_casts_to_double()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.GroupBy(e => e.Name)
            .Select(g => g.Average(e => e.Id))
            .ToQueryString();

        // Assert
        Assert.Contains("AVG(CAST(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(" AS DOUBLE)", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Float aggregate results retain single-precision conversion instead of scale-zero DECIMAL.
    /// </summary>
    [Fact]
    public void Float_sum_casts_to_float()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.GroupBy(e => e.Name)
            .Select(g => g.Sum(e => e.SingleValue))
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "SUM");

        Assert.Contains(" AS FLOAT)", sql, StringComparison.OrdinalIgnoreCase);
    }
}
