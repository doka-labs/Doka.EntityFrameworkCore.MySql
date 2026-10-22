namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Contains the temporal responsibility for MySqlPrimitiveCollectionParameterTests.</summary>
public sealed partial class MySqlPrimitiveCollectionParameterTests
{
    /// <summary>
    /// A DateTimeOffset-to-DateTime converter widens the intermediate mapping
    /// instead of inheriting the property's fractional-seconds precision.
    /// </summary>
    [Fact]
    public void DateTimeOffset_to_datetime_parameter_collection_uses_provider_representation()
    {
        // Arrange
        using var context = CreateContext();
        DateTimeOffset[] values =
        [
            new(2026, 9, 19, 12, 34, 56, TimeSpan.FromHours(2)),
        ];

        // Act
        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Contains(item.ConvertedTimestamp))
            .ToQueryString();

        // Assert
        Assert.Contains("`value` datetime(6) PATH '$'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` datetime(0) PATH '$'", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// DateTime collection elements must retain microseconds even when the
    /// compared property stores whole seconds.
    /// </summary>
    [Fact]
    public void DateTime_parameter_collection_uses_lossless_extraction_facets()
    {
        // Arrange
        using var context = CreateContext();
        var values = new[]
        {
            new DateTime(2026, 9, 19, 12, 34, 56, 500, DateTimeKind.Unspecified),
        };

        // Act
        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Count(value => value == item.OccurredAt) > 0)
            .ToQueryString();

        // Assert
        Assert.Contains("`value` datetime(6) PATH '$'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` datetime(0) PATH '$'", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// DateTime values compared with a date column require a valid, lossless
    /// intermediate type rather than the nonexistent date(6) form.
    /// </summary>
    [Fact]
    public void DateTime_parameter_collection_uses_datetime_for_date_columns()
    {
        // Arrange
        using var context = CreateContext();
        var values = new[] { new DateTime(2000, 1, 2, 12, 0, 0, DateTimeKind.Unspecified) };

        // Act
        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Count(value => value == item.BirthDate) > 0)
            .ToQueryString();

        // Assert
        Assert.Contains("`value` datetime(6) PATH '$'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` date(6) PATH '$'", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Timestamp properties use a timezone-neutral, precision-six
    /// intermediate before the final timestamp comparison.
    /// </summary>
    [Fact]
    public void DateTime_parameter_collection_uses_datetime_for_timestamp_columns()
    {
        // Arrange
        using var context = CreateContext();
        var values = new[]
        {
            new DateTime(2026, 9, 19, 12, 34, 56, 500, DateTimeKind.Utc),
        };

        // Act
        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Count(value => value == item.RecordedAt) > 0)
            .ToQueryString();

        // Assert
        Assert.Contains("`value` datetime(6) PATH '$'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` timestamp(0) PATH '$'", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// TimeOnly collection elements must retain microseconds even when the
    /// compared property stores whole seconds.
    /// </summary>
    [Fact]
    public void TimeOnly_parameter_collection_uses_lossless_extraction_facets()
    {
        // Arrange
        using var context = CreateContext();
        var values = new[] { new TimeOnly(12, 34, 56, 500) };

        // Act
        var sql = context
            .Items
            .Where(item => EF.Parameter(values).Count(value => value == item.StartsAt) > 0)
            .ToQueryString();

        // Assert
        Assert.Contains("`value` time(6) PATH '$'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`value` time(0) PATH '$'", sql, StringComparison.Ordinal);
    }
}
