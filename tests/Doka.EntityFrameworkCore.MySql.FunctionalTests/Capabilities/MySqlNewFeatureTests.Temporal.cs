namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Contains the temporal cases and shared support.</summary>
public sealed partial class MySqlNewFeatureTests
{
    // -- DateOnly/TimeOnly Arithmetic --

    /// <summary>
    /// DateOnly.AddDays translates to DATE_ADD with INTERVAL DAY.
    /// </summary>
    [Fact]
    public void DateOnly_AddDays_translates_to_date_add()
    {
        // Arrange
        using var context = CreateContext<DateTimeContext>();

        // Act
        var query = context
            .Set<DateTimeEntity>()
            .Where(e => e.BirthDate.AddDays(7) > new DateOnly(2025, 1, 1))
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsDateAdd(query, "DAY");
    }

    /// <summary>
    /// DateOnly.AddMonths translates to DATE_ADD with INTERVAL MONTH.
    /// </summary>
    [Fact]
    public void DateOnly_AddMonths_translates_to_date_add()
    {
        // Arrange
        using var context = CreateContext<DateTimeContext>();

        // Act
        var query = context
            .Set<DateTimeEntity>()
            .Where(e => e.BirthDate.AddMonths(3) > new DateOnly(2025, 1, 1))
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsDateAdd(query, "MONTH");
    }

    /// <summary>
    /// DateOnly.AddYears translates to DATE_ADD with INTERVAL YEAR.
    /// </summary>
    [Fact]
    public void DateOnly_AddYears_translates_to_date_add()
    {
        // Arrange
        using var context = CreateContext<DateTimeContext>();

        // Act
        var query = context
            .Set<DateTimeEntity>()
            .Where(e => e.BirthDate.AddYears(1) > new DateOnly(2025, 1, 1))
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsDateAdd(query, "YEAR");
    }

    /// <summary>
    /// TimeOnly.Add(TimeSpan) translates to ADDTIME.
    /// </summary>
    [Fact]
    public void TimeOnly_Add_translates_to_addtime()
    {
        // Arrange
        using var context = CreateContext<DateTimeContext>();

        // Act
        var query = context
            .Set<DateTimeEntity>()
            .Where(e => e.StartTime.Add(TimeSpan.FromHours(1)) > new TimeOnly(12, 0))
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(query, "ADDTIME");
    }
}
