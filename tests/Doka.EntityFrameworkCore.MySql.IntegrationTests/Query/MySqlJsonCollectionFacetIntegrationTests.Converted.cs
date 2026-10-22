namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlJsonCollectionFacetIntegrationTests
{
    /// <summary>Converted JSON enum matches on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Converted_json_enum_matches(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);

        // Act
        var ids = await SelectMarkersAsync(context, value => value.Status == CollectionStatus.Active);

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Absent converted JSON enum does not match on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Absent_converted_json_enum_does_not_match(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);

        // Act
        var ids = await SelectMarkersAsync(context, value => value.Status == (CollectionStatus)42);

        // Assert
        Assert.Empty(ids);
    }

    /// <summary>Full converted JSON decimal precision matches on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Full_converted_json_decimal_precision_matches(
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
            value => value.ConvertedAmount == new PriceValue(ExactAmount));

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Rounded converted JSON decimal does not match on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Rounded_converted_json_decimal_does_not_match(
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
            value => value.ConvertedAmount == new PriceValue(123.5m));

        // Assert
        Assert.Empty(ids);
    }

    /// <summary>Fractional converted JSON duration matches on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Fractional_converted_json_duration_matches(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        var exactDuration = s_exactDuration;

        // Act
        var ids = await SelectMarkersAsync(
            context,
            value => value.ConvertedDuration == new ElapsedValue(exactDuration));

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Different converted JSON duration does not match on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Different_converted_json_duration_does_not_match(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        var exactDuration = s_exactDuration;

        // Act
        var ids = await SelectMarkersAsync(
            context,
            value => value.ConvertedDuration
                == new ElapsedValue(exactDuration.Add(TimeSpan.FromMilliseconds(500))));

        // Assert
        Assert.Empty(ids);
    }

    /// <summary>Converted JSON timestamp matches on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Converted_json_timestamp_matches(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        var exactConvertedTimestamp = s_exactConvertedTimestamp;

        // Act
        var ids = await SelectMarkersAsync(
            context,
            value => value.ConvertedTimestamp == exactConvertedTimestamp);

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Different converted JSON timestamp does not match on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Different_converted_json_timestamp_does_not_match(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        var exactConvertedTimestamp = s_exactConvertedTimestamp;

        // Act
        var ids = await SelectMarkersAsync(
            context,
            value => value.ConvertedTimestamp == exactConvertedTimestamp.AddMilliseconds(500));

        // Assert
        Assert.Empty(ids);
    }

    /// <summary>Converted JSON binary payload matches on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Converted_json_binary_payload_matches(
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
            value => value.ConvertedPayload == new BinaryValue("alpha"));

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Absent converted JSON binary payload does not match on each selected supported engine.</summary>
    /// <param name="targetName">The selected database engine name.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Absent_converted_json_binary_payload_does_not_match(
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
            value => value.ConvertedPayload == new BinaryValue("missing"));

        // Assert
        Assert.Empty(ids);
    }
}
