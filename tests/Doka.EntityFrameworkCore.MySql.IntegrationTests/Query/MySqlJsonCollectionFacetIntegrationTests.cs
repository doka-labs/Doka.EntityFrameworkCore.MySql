namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies that relational facets do not narrow values stored inside owned
/// JSON collections before filtering or materialization.
/// </summary>
[IntegrationDatabaseTest]
public sealed partial class MySqlJsonCollectionFacetIntegrationTests :
    IClassFixture<MySqlJsonCollectionFacetIntegrationTests.SeededDatabaseFixture>
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
    public MySqlJsonCollectionFacetIntegrationTests(
        SeededDatabaseFixture fixture
    ) => _fixture = fixture;

    /// <summary>Full JSON text matches despite short relational facet on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Full_json_text_matches_despite_short_relational_facet(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);

        // Act
        var ids = await SelectMarkersAsync(context, value => value.Text == ExactText);

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Truncated JSON text does not match on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Truncated_json_text_does_not_match(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);

        // Act
        var ids = await SelectMarkersAsync(context, value => value.Text == "alph");

        // Assert
        Assert.Empty(ids);
    }

    /// <summary>Full JSON decimal precision matches on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Full_json_decimal_precision_matches(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);

        // Act
        var ids = await SelectMarkersAsync(context, value => value.Amount == ExactAmount);

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Rounded JSON decimal does not match on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Rounded_json_decimal_does_not_match(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);

        // Act
        var ids = await SelectMarkersAsync(context, value => value.Amount == 123.5m);

        // Assert
        Assert.Empty(ids);
    }

    /// <summary>Fractional JSON DateTime matches on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Fractional_json_datetime_matches(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        var exactDateTime = s_exactDateTime;

        // Act
        var ids = await SelectMarkersAsync(context, value => value.OccurredAt == exactDateTime);

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Truncated JSON DateTime does not match on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Truncated_json_datetime_does_not_match(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);

        // Act
        var ids = await SelectMarkersAsync(
            context,
            value => value.OccurredAt == new DateTime(2026, 9, 19, 12, 34, 56));

        // Assert
        Assert.Empty(ids);
    }

    /// <summary>Time bearing JSON date matches on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Time_bearing_json_date_matches(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        var exactDate = s_exactDate;

        // Act
        var ids = await SelectMarkersAsync(context, value => value.DateValue == exactDate);

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Truncated JSON date does not match on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Truncated_json_date_does_not_match(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        var exactDate = s_exactDate;

        // Act
        var ids = await SelectMarkersAsync(context, value => value.DateValue == exactDate.Date);

        // Assert
        Assert.Empty(ids);
    }

    /// <summary>Fractional JSON TimeOnly matches on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Fractional_json_timeonly_matches(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        var exactTime = s_exactTime;

        // Act
        var ids = await SelectMarkersAsync(context, value => value.StartsAt == exactTime);

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Truncated JSON TimeOnly does not match on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Truncated_json_timeonly_does_not_match(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);

        // Act
        var ids = await SelectMarkersAsync(context, value => value.StartsAt == new TimeOnly(12, 34, 56));

        // Assert
        Assert.Empty(ids);
    }

    /// <summary>Fractional JSON duration matches on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Fractional_json_duration_matches(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        var exactDuration = s_exactDuration;

        // Act
        var ids = await SelectMarkersAsync(context, value => value.Duration == exactDuration);

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Truncated JSON duration does not match on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Truncated_json_duration_does_not_match(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);

        // Act
        var ids = await SelectMarkersAsync(
            context,
            value => value.Duration == TimeSpan.FromHours(27));

        // Assert
        Assert.Empty(ids);
    }

    /// <summary>Binary GUID JSON value matches on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Binary_guid_json_value_matches(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        var exactGuid = s_exactGuid;

        // Act
        var ids = await SelectMarkersAsync(context, value => value.GuidValue == exactGuid);

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Absent binary GUID JSON value does not match on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Absent_binary_guid_json_value_does_not_match(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);

        // Act
        var ids = await SelectMarkersAsync(context, value => value.GuidValue == Guid.Empty);

        // Assert
        Assert.Empty(ids);
    }

    /// <summary>Char GUID JSON value matches on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Char_guid_json_value_matches(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        var exactGuid = s_exactGuid;

        // Act
        var ids = await SelectMarkersAsync(context, value => value.CharGuidValue == exactGuid);

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Absent char GUID JSON value does not match on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Absent_char_guid_json_value_does_not_match(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);

        // Act
        var ids = await SelectMarkersAsync(context, value => value.CharGuidValue == Guid.Empty);

        // Assert
        Assert.Empty(ids);
    }

    /// <summary>JSON binary payload materializes without narrowing on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Json_binary_payload_materializes_without_narrowing(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);

        // Act
        var payload = await context.Owners
            .AsNoTracking()
            .Where(owner => owner.Id == 1)
            .SelectMany(owner => owner.Values)
            .Select(value => value.Payload)
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(s_exactPayload, payload);
    }

    /// <summary>JSON durations outside MySQL time range materialize losslessly on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Json_durations_outside_mysql_time_range_materialize_losslessly(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);

        // Act
        var owner = await context.Owners
            .AsNoTracking()
            .SingleAsync(owner => owner.Id == 2, CancellationToken.None);

        // Assert
        Assert.Equal(
            [s_positiveOutOfRangeDuration, s_negativeOutOfRangeDuration],
            owner.Values.Select(value => value.Duration));
        Assert.Equal(
            [s_positiveOutOfRangeDuration, s_negativeOutOfRangeDuration],
            owner.Values.Select(value => value.ConvertedDuration.Value));
    }
}
