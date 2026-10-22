namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlQueryTranslationExtendedTests
{
    /// <summary>
    /// DateTime.Now translates to microsecond-precision NOW(6).
    /// </summary>
    [Fact]
    public void Datetime_now_translates_to_now()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.Where(e => e.CreatedAt < DateTime.Now)
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsCurrentTimestamp(sql, utc: false, precision: 6);
    }

    /// <summary>
    /// DateTime.UtcNow translates to microsecond-precision UTC_TIMESTAMP(6).
    /// </summary>
    [Fact]
    public void Datetime_utcnow_translates_to_utc_timestamp()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.Where(e => e.CreatedAt < DateTime.UtcNow)
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsCurrentTimestamp(sql, utc: true, precision: 6);
    }

    /// <summary>
    /// DateTime subtraction uses range-preserving integer ticks rather than the
    /// database engine's range-limited TIME result.
    /// </summary>
    [Fact]
    public void Datetime_subtraction_translates_to_timestampdiff_ticks()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.Select(entity => entity.CreatedAt - new DateTime(2000, 1, 1))
            .ToQueryString();

        // Assert
        Assert.Contains("TIMESTAMPDIFF(MICROSECOND", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(") * 10)", sql, StringComparison.Ordinal);
    }
}
