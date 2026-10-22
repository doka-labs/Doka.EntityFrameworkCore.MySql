namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Verifies application-converted collection parameters.</summary>
public sealed partial class MySqlPrimitiveCollectionParameterIntegrationTests
{
    /// <summary>Verifies converted text parameter preserves escaped content on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Converted_text_parameter_preserves_escaped_content(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        TextValue[] values = [new("\"\\q\"")];

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(item => EF.Parameter(values).Contains(item.ConvertedText)));

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Verifies exact decimal parameter matches converted price on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Exact_decimal_parameter_matches_converted_price(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        PriceValue[] values = [new(3.1m)];

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(item => EF.Parameter(values).Contains(item.ConvertedPrice)));

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Verifies overprecise decimal parameter does not match rounded price on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Overprecise_decimal_parameter_does_not_match_rounded_price(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        PriceValue[] values = [new(3.14159m)];

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(item => EF.Parameter(values).Contains(item.ConvertedPrice)));

        // Assert
        Assert.Empty(ids);
    }

    /// <summary>Verifies converted guid parameter matches binary storage on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Converted_guid_parameter_matches_binary_storage(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        StrongGuid[] values = [new(CreateGuid(0))];

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(item => EF.Parameter(values).Contains(item.ConvertedGuid)));

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Verifies absent converted guid parameter matches no rows on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Absent_converted_guid_parameter_matches_no_rows(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        StrongGuid[] values = [new(CreateGuid(RowCount + 1))];

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(item => EF.Parameter(values).Contains(item.ConvertedGuid)));

        // Assert
        Assert.Empty(ids);
    }

    /// <summary>Verifies converted duration parameter matches time storage on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Converted_duration_parameter_matches_time_storage(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        ElapsedValue[] values = [new(TimeSpan.FromHours(27))];

        // Act
        var ids = await SelectIdsAsync(
            context.Items.Where(item => EF.Parameter(values).Contains(item.ConvertedDuration)));

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Verifies subsecond duration parameter does not match zero precision on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Subsecond_duration_parameter_does_not_match_zero_precision(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        ElapsedValue[] values = [new(TimeSpan.FromHours(27).Add(TimeSpan.FromMilliseconds(500)))];

        // Act
        var ids = await SelectIdsAsync(
            context.Items.Where(item => EF.Parameter(values).Contains(item.ConvertedDuration)));

        // Assert
        Assert.Empty(ids);
    }

    /// <summary>Verifies positive out of range duration parameter is rejected on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Positive_out_of_range_duration_parameter_is_rejected(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        ElapsedValue[] values = [new(TimeSpan.FromHours(839))];

        // Act
        var error = await Record.ExceptionAsync(() => SelectIdsAsync(
            context.Items.Where(item => EF.Parameter(values).Contains(item.ConvertedDuration))));

        // Assert
        Assert.Contains("exceeds the MySQL TIME range", Assert.IsType<InvalidOperationException>(error).Message,
            StringComparison.Ordinal);
    }

    /// <summary>Verifies negative out of range duration parameter is rejected on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Negative_out_of_range_duration_parameter_is_rejected(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        ElapsedValue[] values = [new(TimeSpan.FromHours(-839))];

        // Act
        var error = await Record.ExceptionAsync(() => SelectIdsAsync(
            context.Items.Where(item => EF.Parameter(values).Contains(item.ConvertedDuration))));

        // Assert
        Assert.Contains("exceeds the MySQL TIME range", Assert.IsType<InvalidOperationException>(error).Message,
            StringComparison.Ordinal);
    }

    /// <summary>Verifies converted timestamp parameter matches utc storage on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Converted_timestamp_parameter_matches_utc_storage(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        DateTimeOffset[] values = [new(2026, 9, 19, 10, 0, 0, TimeSpan.Zero)];

        // Act
        var ids = await SelectIdsAsync(
            context.Items.Where(item => EF.Parameter(values).Contains(item.ConvertedTimestamp)));

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Verifies subsecond timestamp parameter does not match zero precision on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Subsecond_timestamp_parameter_does_not_match_zero_precision(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        DateTimeOffset[] values = [new DateTimeOffset(2026, 9, 19, 10, 0, 0, TimeSpan.Zero).AddMilliseconds(500)];

        // Act
        var ids = await SelectIdsAsync(
            context.Items.Where(item => EF.Parameter(values).Contains(item.ConvertedTimestamp)));

        // Assert
        Assert.Empty(ids);
    }

    /// <summary>Verifies converted binary parameter matches payload on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Converted_binary_parameter_matches_payload(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        BinaryValue[] values = [new("payload-0")];

        // Act
        var ids = await SelectIdsAsync(
            context.Items.Where(item => EF.Parameter(values).Contains(item.ConvertedPayload)));

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Verifies absent converted binary parameter matches no rows on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Absent_converted_binary_parameter_matches_no_rows(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        BinaryValue[] values = [new("missing")];

        // Act
        var ids = await SelectIdsAsync(
            context.Items.Where(item => EF.Parameter(values).Contains(item.ConvertedPayload)));

        // Assert
        Assert.Empty(ids);
    }
}
