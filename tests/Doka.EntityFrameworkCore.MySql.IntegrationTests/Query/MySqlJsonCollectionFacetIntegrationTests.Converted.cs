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

        await _fixture.EnsureSeededAsync(target, CancellationToken.None);
        await using var context = CreateContext(target);

        // Act
        var ids = await SelectMarkersAsync(
            context,
            value => value.Status == CollectionStatus.Active,
            CancellationToken.None);

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

        await _fixture.EnsureSeededAsync(target, CancellationToken.None);
        await using var context = CreateContext(target);

        // Act
        var ids = await SelectMarkersAsync(
            context,
            value => value.Status == (CollectionStatus)42,
            CancellationToken.None);

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

        await _fixture.EnsureSeededAsync(target, CancellationToken.None);
        await using var context = CreateContext(target);

        // Act
        var ids = await SelectMarkersAsync(
            context,
            value => value.ConvertedAmount == new PriceValue(ExactAmount),
            CancellationToken.None);

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

        await _fixture.EnsureSeededAsync(target, CancellationToken.None);
        await using var context = CreateContext(target);

        // Act
        var ids = await SelectMarkersAsync(
            context,
            value => value.ConvertedAmount == new PriceValue(123.5m),
            CancellationToken.None);

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

        await _fixture.EnsureSeededAsync(target, CancellationToken.None);
        await using var context = CreateContext(target);
        var exactDuration = s_exactDuration;

        // Act
        var ids = await SelectMarkersAsync(
            context,
            value => value.ConvertedDuration == new ElapsedValue(exactDuration),
            CancellationToken.None);

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

        await _fixture.EnsureSeededAsync(target, CancellationToken.None);
        await using var context = CreateContext(target);
        var exactDuration = s_exactDuration;

        // Act
        var ids = await SelectMarkersAsync(
            context,
            value => value.ConvertedDuration
                == new ElapsedValue(exactDuration.Add(TimeSpan.FromMilliseconds(500))),
            CancellationToken.None);

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

        await _fixture.EnsureSeededAsync(target, CancellationToken.None);
        await using var context = CreateContext(target);
        var exactConvertedTimestamp = s_exactConvertedTimestamp;

        // Act
        var ids = await SelectMarkersAsync(
            context,
            value => value.ConvertedTimestamp == exactConvertedTimestamp,
            CancellationToken.None);

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

        await _fixture.EnsureSeededAsync(target, CancellationToken.None);
        await using var context = CreateContext(target);
        var exactConvertedTimestamp = s_exactConvertedTimestamp;

        // Act
        var ids = await SelectMarkersAsync(
            context,
            value => value.ConvertedTimestamp == exactConvertedTimestamp.AddMilliseconds(500),
            CancellationToken.None);

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

        await _fixture.EnsureSeededAsync(target, CancellationToken.None);
        await using var context = CreateContext(target);

        // Act
        var ids = await SelectMarkersAsync(
            context,
            value => value.ConvertedPayload == new BinaryValue("alpha"),
            CancellationToken.None);

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

        await _fixture.EnsureSeededAsync(target, CancellationToken.None);
        await using var context = CreateContext(target);

        // Act
        var ids = await SelectMarkersAsync(
            context,
            value => value.ConvertedPayload == new BinaryValue("missing"),
            CancellationToken.None);

        // Assert
        Assert.Empty(ids);
    }
}
