namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Comprehensive coverage tests for all query translations that were previously untested:
/// DateTime.Add*, TimeSpan members, string methods, math functions, GROUP_CONCAT, edge cases.
/// </summary>
public sealed partial class MySqlQueryTranslationCoverageTests
{
    // -- DateTime.Add* Translations --

    [Fact]
    public void DateTime_AddYears_translates_to_interval_year()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Where(e => e.CreatedAt.AddYears(1) > DateTime.Now)
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsDateAdd(sql, "YEAR");
    }

    [Fact]
    public void DateTime_AddMonths_translates_to_interval_month()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Where(e => e.CreatedAt.AddMonths(6) > DateTime.Now)
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsDateAdd(sql, "MONTH");
    }

    [Fact]
    public void DateTime_AddDays_translates_to_interval_day()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Where(e => e.CreatedAt.AddDays(30) > DateTime.Now)
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsDateAdd(sql, "DAY");
    }

    [Fact]
    public void DateTime_AddHours_translates_to_interval_hour()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Where(e => e.CreatedAt.AddHours(12) > DateTime.Now)
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsDateAdd(sql, "HOUR");
    }

    [Fact]
    public void DateTime_AddMinutes_translates_to_interval_minute()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Where(e => e.CreatedAt.AddMinutes(45) > DateTime.Now)
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsDateAdd(sql, "MINUTE");
    }

    [Fact]
    public void DateTime_AddSeconds_translates_to_interval_second()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Where(e => e.CreatedAt.AddSeconds(90) > DateTime.Now)
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsDateAdd(sql, "SECOND");
    }

    // -- TimeSpan Member Translations --

    [Fact]
    public void TimeSpan_TotalSeconds_translates_to_time_to_sec()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => e.Duration.TotalSeconds)
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "TIME_TO_SEC");
    }

    [Fact]
    public void TimeSpan_TotalMinutes_translates_to_time_to_sec_divided()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => e.Duration.TotalMinutes)
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "TIME_TO_SEC");

        Assert.Contains("60", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void TimeSpan_TotalHours_translates_to_time_to_sec_divided()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => e.Duration.TotalHours)
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "TIME_TO_SEC");

        Assert.Contains("3600", sql, StringComparison.Ordinal);
    }

    // -- DateTime member translations --

    [Fact]
    public void DateTime_Day_translates_to_day_function()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => e.CreatedAt.Day)
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "DAY");
    }

    [Fact]
    public void DateTime_Hour_translates_to_hour_function()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => e.CreatedAt.Hour)
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "HOUR");
    }

    [Fact]
    public void DateTime_Minute_translates_to_minute_function()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => e.CreatedAt.Minute)
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "MINUTE");
    }

    [Fact]
    public void DateTime_Microsecond_translates_to_engine_component()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Select(entity => entity.CreatedAt.Microsecond)
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "MICROSECOND");

        Assert.Contains("% 1000", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void DateTime_Nanosecond_preserves_engine_precision_boundary()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Select(entity => entity.CreatedAt.Nanosecond)
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "MICROSECOND");

        Assert.Contains("* 1000", sql, StringComparison.Ordinal);
        Assert.Contains("% 1000", sql, StringComparison.Ordinal);
    }

    // -- Temporal component and precision translations --

    /// <summary>
    /// Verifies every stored TimeSpan total and component family in a single
    /// server-side projection, including the engines' microsecond boundary.
    /// </summary>
    [Fact]
    public void TimeSpan_components_and_totals_translate_server_side()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => new
            {
                e.Duration.Days,
                e.Duration.Hours,
                e.Duration.Minutes,
                e.Duration.Seconds,
                e.Duration.Milliseconds,
                e.Duration.Microseconds,
                e.Duration.Nanoseconds,
                e.Duration.TotalDays,
                e.Duration.TotalHours,
                e.Duration.TotalMinutes,
                e.Duration.TotalSeconds,
                e.Duration.TotalMilliseconds,
                e.Duration.TotalMicroseconds,
                e.Duration.TotalNanoseconds,
            })
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "TIME_TO_SEC");
        MySqlSqlAssert.ContainsFunction(sql, "MICROSECOND");

        Assert.Contains("1000000000", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that tick-backed TimeSpan members produced by DateTime subtraction
    /// preserve long ranges without routing through the engines' TIME type.
    /// </summary>
    [Fact]
    public void DateTime_difference_components_translate_from_ticks()
    {
        // Arrange
        using var context = CreateContext();
        var baseline = new DateTime(2000, 1, 1);

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => new
            {
                Days = (e.CreatedAt - baseline).Days,
                Hours = (e.CreatedAt - baseline).Hours,
                Minutes = (e.CreatedAt - baseline).Minutes,
                Seconds = (e.CreatedAt - baseline).Seconds,
                Milliseconds = (e.CreatedAt - baseline).Milliseconds,
                Microseconds = (e.CreatedAt - baseline).Microseconds,
                Nanoseconds = (e.CreatedAt - baseline).Nanoseconds,
                TotalDays = (e.CreatedAt - baseline).TotalDays,
                TotalHours = (e.CreatedAt - baseline).TotalHours,
                TotalMinutes = (e.CreatedAt - baseline).TotalMinutes,
                TotalSeconds = (e.CreatedAt - baseline).TotalSeconds,
                TotalMilliseconds = (e.CreatedAt - baseline).TotalMilliseconds,
                TotalMicroseconds = (e.CreatedAt - baseline).TotalMicroseconds,
                TotalNanoseconds = (e.CreatedAt - baseline).TotalNanoseconds,
            })
            .ToQueryString();

        // Assert
        Assert.Contains("TIMESTAMPDIFF(MICROSECOND", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("* 100.0", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("TIME(", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies the actual translator path for a column value. Constant and
    /// parameter Parse inputs are evaluated before SQL generation by EF Core.
    /// </summary>
    [Fact]
    public void DateTime_Parse_from_column_translates_to_str_to_date()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var entity = System.Linq.Expressions.Expression.Parameter(
            typeof(CoverageEntity),
            "entity");

        var name = System.Linq.Expressions.Expression.Property(
            entity,
            nameof(CoverageEntity.Name));

        var createdAt = System.Linq.Expressions.Expression.Property(
            entity,
            nameof(CoverageEntity.CreatedAt));

        var parse = System.Linq.Expressions.Expression.Call(
            typeof(DateTime).GetMethod(
                nameof(DateTime.Parse),
                [typeof(string)])!,
            name);

        var predicate = System.Linq.Expressions.Expression.Lambda<Func<CoverageEntity, bool>>(
            System.Linq.Expressions.Expression.GreaterThanOrEqual(parse, createdAt),
            entity);

        var sql = context
            .Set<CoverageEntity>()
            .Where(predicate)
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "STR_TO_DATE");

        Assert.Contains("%c/%e/%Y %H:%i:%s", sql, StringComparison.Ordinal);
    }
}
