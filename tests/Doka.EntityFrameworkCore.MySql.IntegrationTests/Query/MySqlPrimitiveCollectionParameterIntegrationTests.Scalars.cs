namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Verifies scalar collections and column comparison semantics.</summary>
public sealed partial class MySqlPrimitiveCollectionParameterIntegrationTests
{
    /// <summary>Verifies integer parameter matches first and last rows on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Integer_parameter_matches_first_and_last_rows(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        int[] values = [1, RowCount];

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(item => EF.Parameter(values).Contains(item.Id)));

        // Assert
        Assert.Equal([1, RowCount], ids);
    }

    /// <summary>Verifies string parameter preserves quotes and backslashes on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task String_parameter_preserves_quotes_and_backslashes(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        string[] values = ["alpha", "\"quoted\"", "\"\\q\""];

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(item => EF.Parameter(values).Contains(item.Name)));

        // Assert
        Assert.Equal([1, 2, 3], ids);
    }

    /// <summary>Verifies nullable string parameter matches value and null on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Nullable_string_parameter_matches_value_and_null(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        string?[] values = ["alpha", null];

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(item => EF.Parameter(values).Contains(item.OptionalName)));

        // Assert
        Assert.Equal([1, 2], ids);
    }

    /// <summary>Verifies converted string parameter matches provider representation on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Converted_string_parameter_matches_provider_representation(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        string[] values = ["alpha"];

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(item => EF.Parameter(values).Contains(item.ConvertedName)));

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Verifies converted string parameter respects binary collation on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Converted_string_parameter_respects_binary_collation(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        string[] values = ["Alpha"];

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(item => EF.Parameter(values).Contains(item.ConvertedName)));

        // Assert
        Assert.Empty(ids);
    }

    /// <summary>Verifies case insensitive string parameter uses column collation on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Case_insensitive_string_parameter_uses_column_collation(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        string[] values = ["Alpha"];

        // Act
        var ids = await SelectIdsAsync(
            context.Items.Where(item => EF.Parameter(values).Contains(item.CaseInsensitiveName)));

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Verifies enum parameter matches converted string on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Enum_parameter_matches_converted_string(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        CollectionStatus[] values = [CollectionStatus.Active];

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(item => EF.Parameter(values).Contains(item.Status)));

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Verifies unknown enum parameter matches no rows on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Unknown_enum_parameter_matches_no_rows(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        CollectionStatus[] values = [(CollectionStatus)42];

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(item => EF.Parameter(values).Contains(item.Status)));

        // Assert
        Assert.Empty(ids);
    }

    /// <summary>Verifies nullable enum parameter matches value and null on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Nullable_enum_parameter_matches_value_and_null(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        CollectionStatus?[] values = [CollectionStatus.Active, null];

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(item => EF.Parameter(values).Contains(item.OptionalStatus)));

        // Assert
        Assert.Equal([1, 2], ids);
    }

    /// <summary>Verifies binary collation rejects differently cased name on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Binary_collation_rejects_differently_cased_name(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        string[] values = ["Alpha"];

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(item => EF.Parameter(values).Contains(item.Name)));

        // Assert
        Assert.Empty(ids);
    }

    /// <summary>Verifies binary payload parameter matches selected rows on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Binary_payload_parameter_matches_selected_rows(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);
        byte[][] values = [BitConverter.GetBytes(0), BitConverter.GetBytes(2)];

        // Act
        var ids = await SelectIdsAsync(context.Items.Where(item => EF.Parameter(values).Contains(item.Payload)));

        // Assert
        Assert.Equal([1, 3], ids);
    }

    /// <summary>Verifies decoded string expression has coercible collation on each engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Decoded_string_expression_has_coercible_collation(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target);
        await using var context = CreateContext(target);

        // Act
        var coercibility = await ReadStringDecodeCoercibilityAsync(context);

        // Assert
        Assert.True(
            coercibility > 2,
            $"Expected coercible string decoding, but COERCIBILITY returned {coercibility}.");
    }
}
