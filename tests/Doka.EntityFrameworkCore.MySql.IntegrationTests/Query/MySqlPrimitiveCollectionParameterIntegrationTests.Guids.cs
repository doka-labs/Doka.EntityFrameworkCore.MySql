namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Verifies binary and textual GUID collection parameters.</summary>
public sealed partial class MySqlPrimitiveCollectionParameterIntegrationTests
{
    /// <summary>Verifies binary guid single parameter matches one row on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Binary_guid_single_parameter_matches_one_row(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        Guid[] values = [CreateGuid(0)];

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(item => EF.Parameter(values).Contains(item.BinaryId)));

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Verifies binary guid three parameters match three rows on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Binary_guid_three_parameters_match_three_rows(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        Guid[] values = [CreateGuid(0), CreateGuid(1), CreateGuid(2)];

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(item => EF.Parameter(values).Contains(item.BinaryId)));

        // Assert
        Assert.Equal([1, 2, 3], ids);
    }

    /// <summary>Verifies binary guid large parameter matches all rows on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Binary_guid_large_parameter_matches_all_rows(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        var values = Enumerable.Range(0, RowCount).Select(CreateGuid).ToArray();

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(item => EF.Parameter(values).Contains(item.BinaryId)));

        // Assert
        Assert.Equal(Enumerable.Range(1, RowCount), ids);
    }

    /// <summary>Verifies char guid parameter matches selected rows on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Char_guid_parameter_matches_selected_rows(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        Guid[] values = [CreateGuid(0), CreateGuid(2)];

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(item => values.Contains(item.CharId)));

        // Assert
        Assert.Equal([1, 3], ids);
    }

    /// <summary>Verifies empty guid parameter matches no rows on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Empty_guid_parameter_matches_no_rows(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        Guid[] values = [];

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(item => values.Contains(item.BinaryId)));

        // Assert
        Assert.Empty(ids);
    }

    /// <summary>Verifies duplicate and absent guid parameters match once on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Duplicate_and_absent_guid_parameters_match_once(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        Guid[] values = [CreateGuid(0), CreateGuid(0), CreateGuid(RowCount + 1)];

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(item => values.Contains(item.BinaryId)));

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Verifies nullable binary guid parameter matches value and null on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Nullable_binary_guid_parameter_matches_value_and_null(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        Guid?[] values = [CreateGuid(0), null];

        // Act
        var ids = await SelectIdsAsync(
            context.Items.Where(item => EF.Parameter(values).Contains(item.OptionalBinaryId)));

        // Assert
        Assert.Equal([1, 2], ids);
    }

    /// <summary>Verifies nullable char guid parameter matches value and null on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Nullable_char_guid_parameter_matches_value_and_null(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        Guid?[] values = [CreateGuid(0), null];

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(item => EF.Parameter(values).Contains(item.OptionalCharId)));

        // Assert
        Assert.Equal([1, 2], ids);
    }

    /// <summary>Verifies default guid contains matches selected rows on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Default_guid_contains_matches_selected_rows(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        Guid[] values = [CreateGuid(1), CreateGuid(2)];

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(item => values.Contains(item.BinaryId)));

        // Assert
        Assert.Equal([2, 3], ids);
    }

    /// <summary>Verifies scalar guid equality matches one row on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Scalar_guid_equality_matches_one_row(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(item => item.BinaryId == CreateGuid(0)));

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Verifies binary guid model collection round trips on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Binary_guid_model_collection_round_trips(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);

        // Act
        // Project the decoded rowset directly, without correlated-IN optimizer semantics.
        var values = await context.Items
            .Where(item => item.Id == 11)
            .SelectMany(item => item.GuidValues)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([CreateGuid(10), Guid.Empty], values);
    }

    /// <summary>Verifies char guid model collection round trips on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Char_guid_model_collection_round_trips(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateCharContext(target);

        // Act
        var values = await context.Items
            .SelectMany(item => item.GuidValues)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([CreateGuid(10), Guid.Empty], values);
    }
}
