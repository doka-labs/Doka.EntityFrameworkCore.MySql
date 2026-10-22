namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Contains the queries responsibility for MySqlMigrationDdlCoverageTests.</summary>
public sealed partial class MySqlMigrationDdlCoverageTests
{
    // -- JSON translation gaps --

    [Fact]
    public void JsonReplace_translates_to_json_replace_sql()
    {
        // Arrange
        using var context = CreateJsonContext();

        // Act
        var sql = context
            .Set<JsonEntity>()
            .Select(e => EF.Functions.JsonReplace(e.Data, "$.name", "updated"))
            .ToQueryString();

        // Assert
        Assert.Contains("JSON_REPLACE", sql, StringComparison.OrdinalIgnoreCase);
    }

    // -- TimeOnly.AddHours / AddMinutes --

    [Fact]
    public void TimeOnly_AddHours_translates_to_interval_hour()
    {
        // Arrange
        using var context = CreateTemporalContext();

        // Act
        var sql = context
            .Set<TemporalEntity>()
            .Where(e => e.StartTime.AddHours(2) > new TimeOnly(14, 0))
            .ToQueryString();

        // Assert
        Assert.Contains("INTERVAL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("HOUR", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TimeOnly_AddMinutes_translates_to_interval_minute()
    {
        // Arrange
        using var context = CreateTemporalContext();

        // Act
        var sql = context
            .Set<TemporalEntity>()
            .Where(e => e.StartTime.AddMinutes(30) > new TimeOnly(14, 0))
            .ToQueryString();

        // Assert
        Assert.Contains("INTERVAL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MINUTE", sql, StringComparison.OrdinalIgnoreCase);
    }
}
