namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlQueryTranslationExtendedTests
{
    /// <summary>
    /// AutoInclude + IgnoreAutoIncludes -- navigation configuration.
    /// </summary>
    [Fact]
    public void Auto_include_can_be_configured()
    {
        // Arrange
        using var context = new AutoIncludeContext(CreateAutoIncludeOptions());

        // Act
        var sql = context
            .Set<AutoIncludeBlog>()
            .ToQueryString();

        // Assert
        Assert.Contains("JOIN", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// IgnoreAutoIncludes removes auto-include.
    /// </summary>
    [Fact]
    public void Ignore_auto_includes_removes_join()
    {
        // Arrange
        using var context = new AutoIncludeContext(CreateAutoIncludeOptions());

        // Act
        var sql = context
            .Set<AutoIncludeBlog>()
            .IgnoreAutoIncludes()
            .ToQueryString();

        // Assert
        Assert.DoesNotContain("JOIN", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies filtered include produces valid sql.</summary>
    [Fact]
    public void Filtered_include_produces_valid_sql()
    {
        // Arrange
        using var context = new AutoIncludeContext(CreateAutoIncludeOptions());

        // Act
        var sql = context
            .Set<AutoIncludeBlog>()
            .IgnoreAutoIncludes()
            .Include(b => b.Posts.Where(p => p.Content.Length > 10))
            .ToQueryString();

        // Assert
        Assert.Contains("JOIN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
    }
}
