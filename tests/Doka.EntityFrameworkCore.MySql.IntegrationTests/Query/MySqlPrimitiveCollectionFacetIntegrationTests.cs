namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies that parameterized primitive collections are extracted without
/// applying lossy property facets before comparison.
/// </summary>
[IntegrationDatabaseTest]
public sealed partial class MySqlPrimitiveCollectionFacetIntegrationTests :
    IClassFixture<MySqlPrimitiveCollectionFacetIntegrationTests.SeededDatabaseFixture>
{
    private readonly SeededDatabaseFixture _fixture;

    /// <summary>Returns the selected engines in the supported six-engine contract.</summary>
    // WHY: the legacy MySQL 8.0 opt-in target is outside this supported-engine matrix.
    public static IEnumerable<object[]> SelectedTargets => IntegrationTestEnvironment
        .GetSelectedTargets()
        .Where(target => IntegrationTestEnvironment.GetSupportedTargets().Contains(target))
        .Select(target => new object[] { target.ToString() });

    /// <summary>Creates an independent test case sharing only immutable seeded rows.</summary>
    /// <param name="fixture">The class-owned per-engine database fixture.</param>
    public MySqlPrimitiveCollectionFacetIntegrationTests(
        SeededDatabaseFixture fixture
    )
    {
        _fixture = fixture;
    }

    /// <summary>Exact decimal parameter matches stored value on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Exact_decimal_parameter_matches_stored_value(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        var exactDecimals = new[] { 3.1m };

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(
            item => EF.Parameter(exactDecimals).Count(value => value == item.Price) > 0));

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Extra decimal precision does not match rounded property on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Extra_decimal_precision_does_not_match_rounded_property(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        var truncatedDecimals = new[] { 3.14159m };

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(
            item => EF.Parameter(truncatedDecimals).Count(value => value == item.Price) > 0));

        // Assert
        Assert.Empty(ids);
    }

    /// <summary>Exact DateTime parameter matches stored value on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Exact_datetime_parameter_matches_stored_value(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        var exactDateTimes = new[] { s_exactDateTime };

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(
            item => EF.Parameter(exactDateTimes).Count(value => value == item.OccurredAt) > 0));

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Fractional DateTime does not match second precision property on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Fractional_datetime_does_not_match_second_precision_property(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        var truncatedDateTimes = new[] { s_exactDateTime.AddMilliseconds(500) };

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(
            item => EF.Parameter(truncatedDateTimes).Count(value => value == item.OccurredAt) > 0));

        // Assert
        Assert.Empty(ids);
    }

    /// <summary>Exact nullable DateTime parameter matches stored value on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Exact_nullable_datetime_parameter_matches_stored_value(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        DateTime?[] exactNullableDateTimes = [s_exactDateTime];

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(
            item => EF.Parameter(exactNullableDateTimes).Contains(item.OptionalOccurredAt)));

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Out of range nullable DateTime does not match on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Out_of_range_nullable_datetime_does_not_match(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        DateTime?[] outOfRangeNullableDateTimes = [DateTime.MinValue];

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(
            item => EF.Parameter(outOfRangeNullableDateTimes).Contains(item.OptionalOccurredAt)));

        // Assert
        Assert.Empty(ids);
    }

    /// <summary>Exact timestamp parameter matches stored value on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Exact_timestamp_parameter_matches_stored_value(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        var exactTimestamps = new[] { s_exactTimestamp };

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(
            item => EF.Parameter(exactTimestamps).Count(value => value == item.RecordedAt) > 0));

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Fractional timestamp does not match second precision property on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Fractional_timestamp_does_not_match_second_precision_property(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        var truncatedTimestamps = new[] { s_exactTimestamp.AddMilliseconds(500) };

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(
            item => EF.Parameter(truncatedTimestamps).Count(value => value == item.RecordedAt) > 0));

        // Assert
        Assert.Empty(ids);
    }

    /// <summary>Out of range DateTime does not match on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Out_of_range_datetime_does_not_match(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        var outOfRangeDateTimes = new[] { DateTime.MinValue };

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(
            item => EF.Parameter(outOfRangeDateTimes).Count(value => value == item.OccurredAt) > 0));

        // Assert
        Assert.Empty(ids);
    }

    /// <summary>Exact date parameter matches stored value on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Exact_date_parameter_matches_stored_value(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        var exactDates = new[] { s_birthDate };

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(
            item => EF.Parameter(exactDates).Count(value => value == item.BirthDate) > 0));

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Time bearing DateTime does not match date property on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Time_bearing_datetime_does_not_match_date_property(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        var timeBearingDates = new[] { s_birthDate.AddHours(12) };

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(
            item => EF.Parameter(timeBearingDates).Count(value => value == item.BirthDate) > 0));

        // Assert
        Assert.Empty(ids);
    }

    /// <summary>Exact DateOnly parameter matches stored value on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Exact_dateonly_parameter_matches_stored_value(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        var exactCalendarDates = new[] { s_calendarDate };

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(
            item => EF.Parameter(exactCalendarDates).Contains(item.CalendarDate)));

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Absent DateOnly parameter does not match on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Absent_dateonly_parameter_does_not_match(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        var absentCalendarDates = new[] { s_calendarDate.AddYears(1) };

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(
            item => EF.Parameter(absentCalendarDates).Contains(item.CalendarDate)));

        // Assert
        Assert.Empty(ids);
    }

    /// <summary>Exact nullable DateOnly parameter matches stored value on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Exact_nullable_dateonly_parameter_matches_stored_value(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        DateOnly?[] exactNullableCalendarDates = [s_calendarDate];

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(
            item => EF.Parameter(exactNullableCalendarDates).Contains(item.OptionalCalendarDate)));

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Out of range nullable DateOnly does not match on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Out_of_range_nullable_dateonly_does_not_match(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        DateOnly?[] outOfRangeNullableCalendarDates = [DateOnly.MinValue];

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(
            item => EF.Parameter(outOfRangeNullableCalendarDates).Contains(item.OptionalCalendarDate)));

        // Assert
        Assert.Empty(ids);
    }

    /// <summary>Exact TimeOnly parameter matches stored value on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Exact_timeonly_parameter_matches_stored_value(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        var exactTimeOnlyValues = new[] { s_exactTimeOnly };

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(
            item => EF.Parameter(exactTimeOnlyValues).Count(value => value == item.StartsAt) > 0));

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Fractional TimeOnly does not match second precision property on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Fractional_timeonly_does_not_match_second_precision_property(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        var truncatedTimeOnlyValues = new[] { s_exactTimeOnly.Add(TimeSpan.FromMilliseconds(500)) };

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(
            item => EF.Parameter(truncatedTimeOnlyValues).Count(value => value == item.StartsAt) > 0));

        // Assert
        Assert.Empty(ids);
    }

    /// <summary>Exact duration parameter matches stored value on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Exact_duration_parameter_matches_stored_value(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        var exactTimeSpanValues = new[] { s_exactTimeSpan };

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(
            item => EF.Parameter(exactTimeSpanValues).Count(value => value == item.Duration) > 0));

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Fractional duration does not match second precision property on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Fractional_duration_does_not_match_second_precision_property(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        var truncatedTimeSpanValues = new[] { s_exactTimeSpan.Add(TimeSpan.FromMilliseconds(500)) };

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(
            item => EF.Parameter(truncatedTimeSpanValues).Count(value => value == item.Duration) > 0));

        // Assert
        Assert.Empty(ids);
    }

    /// <summary>Exact nullable duration parameter matches stored value on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Exact_nullable_duration_parameter_matches_stored_value(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        TimeSpan?[] exactNullableTimeSpanValues = [s_exactTimeSpan];

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(
            item => EF.Parameter(exactNullableTimeSpanValues).Contains(item.OptionalDuration)));

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Negative duration parameter matches stored value on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Negative_duration_parameter_matches_stored_value(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        var negativeTimeSpanValues = new[] { s_negativeTimeSpan };

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(
            item => EF.Parameter(negativeTimeSpanValues).Count(value => value == item.Duration) > 0));

        // Assert
        Assert.Equal([2], ids);
    }

    /// <summary>Duration above MySQL time range is rejected on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Duration_above_mysql_time_range_is_rejected(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        var positiveOutOfRangeTimeSpanValues = new[] { TimeSpan.FromHours(839) };

        // Act
        var exception = await Record.ExceptionAsync(() => SelectIdsAsync(context.Items.Where(
            item => EF.Parameter(positiveOutOfRangeTimeSpanValues)
                .Count(value => value == item.Duration) > 0)));

        // Assert
        Assert.Contains(
            "exceeds the MySQL TIME range",
            Assert.IsType<InvalidOperationException>(exception).Message,
            StringComparison.Ordinal);
    }

    /// <summary>Duration below MySQL time range is rejected on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Duration_below_mysql_time_range_is_rejected(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        var negativeOutOfRangeTimeSpanValues = new[] { TimeSpan.FromHours(-839) };

        // Act
        var exception = await Record.ExceptionAsync(() => SelectIdsAsync(context.Items.Where(
            item => EF.Parameter(negativeOutOfRangeTimeSpanValues)
                .Count(value => value == item.Duration) > 0)));

        // Assert
        Assert.Contains(
            "exceeds the MySQL TIME range",
            Assert.IsType<InvalidOperationException>(exception).Message,
            StringComparison.Ordinal);
    }
}
