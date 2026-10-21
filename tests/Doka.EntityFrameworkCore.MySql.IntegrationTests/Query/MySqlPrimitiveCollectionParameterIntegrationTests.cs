namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies parameterized primitive collections against the stored provider
/// representation on every supported database engine.
/// </summary>
[IntegrationDatabaseTest]
public sealed class MySqlPrimitiveCollectionParameterIntegrationTests :
    IClassFixture<MySqlPrimitiveCollectionParameterIntegrationTests.SeededDatabaseFixture>
{
    private const string TableName = "IntPrimitiveCollectionItems";
    private const string CharTableName = "IntCharGuidCollectionItems";
    private const string SqlModeTableName = "IntSqlModeStringCollectionItems";
    private const int RowCount = 2200;

    private readonly SeededDatabaseFixture _fixture;

    /// <summary>Returns one independently reported case per selected supported engine.</summary>
    // WHY: MySql80 is a legacy opt-in target, not part of this six-engine contract.
    // The companion theory attribute skips a run that selects only that target.
    public static IEnumerable<object[]> SelectedTargets => IntegrationTestEnvironment
        .GetSelectedTargets()
        .Where(target => IntegrationTestEnvironment.GetSupportedTargets().Contains(target))
        .Select(target => new object[] { target.ToString() });

    /// <summary>Creates a test case with the shared per-engine seed fixture.</summary>
    /// <param name="fixture">The class-owned, lazily seeded database fixture.</param>
    public MySqlPrimitiveCollectionParameterIntegrationTests(
        SeededDatabaseFixture fixture
    ) => _fixture = fixture;

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

    /// <summary>Verifies string parameters under MariaDB's no-backslash SQL mode.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_no_backslash_escapes_matches_string_parameters()
    {
        // Arrange
        await _fixture.EnsureSqlModeSeededAsync();
        string[] values = ["\"quoted\"", "\"\\q\"", string.Empty, "gr\u00FCn"];

        // Act
        var ids = await ExecuteWithNoBackslashEscapesAsync(context => context.Items
            .Where(item => EF.Parameter(values).Contains(item.Value))
            .OrderBy(item => item.Id)
            .Select(item => item.Id)
            .ToArrayAsync(CancellationToken.None));

        // Assert
        Assert.Equal([1, 2, 3, 4], ids);
    }

    /// <summary>Verifies nullable string parameters under MariaDB's no-backslash SQL mode.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_no_backslash_escapes_matches_nullable_string_parameters()
    {
        // Arrange
        await _fixture.EnsureSqlModeSeededAsync();
        string?[] values = ["\"quoted\"", null];

        // Act
        var ids = await ExecuteWithNoBackslashEscapesAsync(context => context.Items
            .Where(item => EF.Parameter(values).Contains(item.OptionalValue))
            .OrderBy(item => item.Id)
            .Select(item => item.Id)
            .ToArrayAsync(CancellationToken.None));

        // Assert
        Assert.Equal([1, 2], ids);
    }

    /// <summary>Verifies converted string parameters under MariaDB's no-backslash SQL mode.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_no_backslash_escapes_matches_converted_string_parameters()
    {
        // Arrange
        await _fixture.EnsureSqlModeSeededAsync();
        string[] values = ["alpha"];

        // Act
        var ids = await ExecuteWithNoBackslashEscapesAsync(context => context.Items
            .Where(item => EF.Parameter(values).Contains(item.ConvertedValue))
            .Select(item => item.Id)
            .ToArrayAsync(CancellationToken.None));

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Verifies converted text parameters under MariaDB's no-backslash SQL mode.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_no_backslash_escapes_matches_converted_text_parameters()
    {
        // Arrange
        await _fixture.EnsureSqlModeSeededAsync();
        TextValue[] values = [new("\"\\q\""), new("gr\u00FCn")];

        // Act
        var ids = await ExecuteWithNoBackslashEscapesAsync(context => context.Items
            .Where(item => EF.Parameter(values).Contains(item.ConvertedText))
            .OrderBy(item => item.Id)
            .Select(item => item.Id)
            .ToArrayAsync(CancellationToken.None));

        // Assert
        Assert.Equal([2, 4], ids);
    }

    /// <summary>Verifies model string collections under MariaDB's no-backslash SQL mode.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_no_backslash_escapes_preserves_model_string_collections()
    {
        // Arrange
        await _fixture.EnsureSqlModeSeededAsync();
        string?[] expected = ["\"quoted\"", "\"\\q\"", string.Empty, "gr\u00FCn", null];

        // Act
        var values = await ExecuteWithNoBackslashEscapesAsync(context => context.Items
            .Where(item => item.Id == 1)
            .SelectMany(item => item.Values)
            .ToArrayAsync(CancellationToken.None));

        // Assert
        Assert.Equal(expected, values);
    }

    /// <summary>
    /// Verifies ordinary scalar string parameters when the connection's
    /// escaping contract matches the server's NO_BACKSLASH_ESCAPES mode.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_default_scalar_strings_match_configured_sql_mode()
    {
        // Arrange
        await _fixture.EnsureSqlModeSeededAsync();
        string[] values = ["\"\\q\"", "missing"];

        // Act
        var (sql, ids) = await ExecuteWithNoBackslashEscapesAsync(async context =>
        {
            var query = context.Items.Where(item => values.Contains(item.Value));

            return (
                query.ToQueryString(),
                await query.Select(item => item.Id).ToArrayAsync(CancellationToken.None));
        }, configureConnector: true);

        // Assert
        Assert.Contains(" IN (", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_TABLE", sql, StringComparison.Ordinal);
        Assert.Equal([2], ids);
    }

    // WHY: both factories stay in this file because the caller path owns the
    // isolated schema, including calls made from the class fixture.
    private static PrimitiveCollectionContext CreateContext(
        IntegrationDatabaseTarget target
    )
    {
        var builder = IntegrationTestDbContextOptions.Create<PrimitiveCollectionContext>();
        builder.UseMySql(
            IntegrationTestEnvironment.GetConnectionString(target),
            IntegrationTestEnvironment.GetServerVersion(target),
            options => options.DefaultGuidFormat(MySqlGuidFormat.Binary16));

        return new PrimitiveCollectionContext(builder.Options);
    }

    private static CharGuidCollectionContext CreateCharContext(
        IntegrationDatabaseTarget target
    )
    {
        var builder = IntegrationTestDbContextOptions.Create<CharGuidCollectionContext>();
        builder.UseMySql(
            IntegrationTestEnvironment.GetConnectionString(target),
            IntegrationTestEnvironment.GetServerVersion(target),
            options => options.DefaultGuidFormat(MySqlGuidFormat.Char36));

        return new CharGuidCollectionContext(builder.Options);
    }

    private static SqlModeStringCollectionContext CreateSqlModeContext(
        bool configureConnector = false
    )
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb118);

        if (configureConnector)
        {
            connectionString = new MySqlConnectionStringBuilder(connectionString)
            {
                NoBackslashEscapes = true,
            }.ConnectionString;
        }

        var builder = IntegrationTestDbContextOptions.Create<SqlModeStringCollectionContext>();
        builder.UseMySql(
            connectionString,
            IntegrationTestEnvironment.GetServerVersion(IntegrationDatabaseTarget.MariaDb118));

        return new SqlModeStringCollectionContext(builder.Options);
    }

    private static async Task<TResult> ExecuteWithNoBackslashEscapesAsync<TResult>(
        Func<SqlModeStringCollectionContext, Task<TResult>> execute,
        bool configureConnector = false
    )
    {
        await using var context = CreateSqlModeContext(configureConnector);
        await context.Database.OpenConnectionAsync(CancellationToken.None);

        var connection = context.Database.GetDbConnection();
        var previousSqlMode = await ReadSqlModeAsync(connection);

        try
        {
            await SetSqlModeAsync(connection, "NO_BACKSLASH_ESCAPES");

            return await execute(context);
        }
        finally
        {
            // WHY: sql_mode is session state; always restore it before this
            // connection can return to MySqlConnector's pool.
            await SetSqlModeAsync(connection, previousSqlMode);
        }
    }

    /// <summary>
    /// Owns one lazily seeded, class-scoped schema per selected engine.
    /// </summary>
    public sealed class SeededDatabaseFixture : IAsyncLifetime
    {
        private readonly Dictionary<IntegrationDatabaseTarget, Task> _seeds = new();
        private Task? _sqlModeSeed;

        /// <summary>Leaves seeding to the first selected test for each engine.</summary>
        public Task InitializeAsync() => Task.CompletedTask;

        /// <summary>Ensures the 2,200-row fixture is written once for the selected engine.</summary>
        /// <param name="target">The selected database engine.</param>
        /// <returns>A task that completes when both primitive-collection tables are ready.</returns>
        internal Task EnsureSeededAsync(
            IntegrationDatabaseTarget target
        )
        {
            // WHY: xUnit serializes methods in this class. Retaining the Task
            // also retains seed failures, so later cases do not retry partial DDL.
            if (!_seeds.TryGetValue(target, out var seed))
            {
                seed = SeedAsync(target);
                _seeds.Add(target, seed);
            }

            return seed;
        }

        /// <summary>Ensures the MariaDB SQL-mode rows are written once for this class.</summary>
        internal Task EnsureSqlModeSeededAsync() => _sqlModeSeed ??= SeedSqlModeAsync();

        /// <summary>Drops every table created by this class, including after test failure.</summary>
        public async Task DisposeAsync()
        {
            List<Exception>? failures = null;

            foreach (var target in _seeds.Keys)
            {
                // WHY: a partially initialized engine may fail to drop one table;
                // the other table and remaining engines still need cleanup.
                try
                {
                    await using var charContext = CreateCharContext(target);
                    await DropCharTableAsync(charContext);
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(
                        new InvalidOperationException($"Could not drop {CharTableName} on {target}.", exception));
                }

                try
                {
                    await using var context = CreateContext(target);
                    await DropTableAsync(context);
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(
                        new InvalidOperationException($"Could not drop {TableName} on {target}.", exception));
                }
            }

            if (_sqlModeSeed is not null)
            {
                try
                {
                    await using var context = CreateSqlModeContext();
                    await DropSqlModeTableAsync(context);
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(
                        new InvalidOperationException($"Could not drop {SqlModeTableName} on MariaDb118.", exception));
                }
            }

            if (failures is not null)
            {
                throw new AggregateException("Primitive collection fixture cleanup failed.", failures);
            }
        }

        private static async Task SeedSqlModeAsync()
        {
            await using var context = CreateSqlModeContext();
            await DropSqlModeTableAsync(context);
            await context.Database.ExecuteSqlRawAsync(
                context.Database.GenerateCreateScript(),
                CancellationToken.None);
            context.AddRange(
                new SqlModeStringCollectionEntity
                {
                    Id = 1,
                    Value = "\"quoted\"",
                    OptionalValue = null,
                    ConvertedValue = "alpha",
                    ConvertedText = new TextValue("\"quoted\""),
                    Values = ["\"quoted\"", "\"\\q\"", string.Empty, "gr\u00FCn", null],
                },
                new SqlModeStringCollectionEntity
                {
                    Id = 2,
                    Value = "\"\\q\"",
                    OptionalValue = "\"quoted\"",
                    ConvertedValue = "beta",
                    ConvertedText = new TextValue("\"\\q\""),
                },
                new SqlModeStringCollectionEntity
                {
                    Id = 3,
                    Value = string.Empty,
                    OptionalValue = "other",
                    ConvertedValue = "gamma",
                    ConvertedText = new TextValue(string.Empty),
                },
                new SqlModeStringCollectionEntity
                {
                    Id = 4,
                    Value = "gr\u00FCn",
                    OptionalValue = "other",
                    ConvertedValue = "delta",
                    ConvertedText = new TextValue("gr\u00FCn"),
                });
            await context.SaveChangesAsync(CancellationToken.None);
        }

        private static async Task SeedAsync(
            IntegrationDatabaseTarget target
        )
        {
            await using var context = CreateContext(target);
            await using var charContext = CreateCharContext(target);
            await DropTableAsync(context);
            await DropCharTableAsync(charContext);

            // WHY: each independent assertion needs the same large cardinality;
            // one seed per engine avoids multiplying schema DDL and 2,200 inserts.
            var entities = Enumerable
                .Range(0, RowCount)
                .Select(index => new PrimitiveCollectionEntity
                {
                    Id = index + 1,
                    BinaryId = CreateGuid(index),
                    OptionalBinaryId = index == 1 ? null : CreateGuid(index),
                    OptionalCharId = index == 1 ? null : CreateGuid(index),
                    CharId = CreateGuid(index),
                    Name = index switch
                    {
                        0 => "alpha",
                        1 => "\"quoted\"",
                        2 => "\"\\q\"",
                        _ => $"value-{index}",
                    },
                    OptionalName = index == 1 ? null : index == 0 ? "alpha" : $"optional-{index}",
                    ConvertedName = index switch
                    {
                        0 => "alpha",
                        1 => "beta",
                        _ => $"converted-{index}",
                    },
                    CaseInsensitiveName = index == 0 ? "alpha" : $"case-{index}",
                    ConvertedText = new TextValue(index switch
                    {
                        0 => "\"\\q\"",
                        1 => "gr\u00FCn",
                        _ => $"text-{index}",
                    }),
                    Status = index == 0 ? CollectionStatus.Active : CollectionStatus.Inactive,
                    OptionalStatus = index == 1 ? null : index == 0
                        ? CollectionStatus.Active
                        : CollectionStatus.Inactive,
                    ConvertedPrice = index == 0 ? new PriceValue(3.1m) : new PriceValue(index),
                    ConvertedGuid = new StrongGuid(CreateGuid(index)),
                    ConvertedDuration = index == 0
                        ? new ElapsedValue(TimeSpan.FromHours(27))
                        : new ElapsedValue(TimeSpan.FromHours(index % 24)),
                    ConvertedTimestamp = new DateTimeOffset(
                        2026,
                        9,
                        19,
                        10,
                        0,
                        0,
                        TimeSpan.Zero).AddSeconds(index),
                    ConvertedPayload = new BinaryValue($"payload-{index}"),
                    Payload = BitConverter.GetBytes(index),
                    GuidValues = [CreateGuid(index), Guid.Empty],
                })
                .ToArray();

            await context.Database.ExecuteSqlRawAsync(
                context.Database.GenerateCreateScript(),
                CancellationToken.None);
            context.Items.AddRange(entities);
            await context.SaveChangesAsync(CancellationToken.None);
            context.ChangeTracker.Clear();

            await charContext.Database.ExecuteSqlRawAsync(
                charContext.Database.GenerateCreateScript(),
                CancellationToken.None);
            charContext.Add(
                new CharGuidCollectionEntity
                {
                    Id = 1,
                    GuidValues = [CreateGuid(10), Guid.Empty],
                });
            await charContext.SaveChangesAsync(CancellationToken.None);
            charContext.ChangeTracker.Clear();
        }
    }

    private static async Task<int[]> SelectIdsAsync(
        IQueryable<PrimitiveCollectionEntity> query
    ) => await query
        .OrderBy(item => item.Id)
        .Select(item => item.Id)
        .ToArrayAsync(CancellationToken.None);

    private static Guid CreateGuid(
        int value
    ) => new(value, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static async Task DropTableAsync(
        PrimitiveCollectionContext context
    ) => await context.Database.ExecuteSqlRawAsync(
        $"DROP TABLE IF EXISTS `{TableName}`;",
        CancellationToken.None);

    private static async Task DropCharTableAsync(
        CharGuidCollectionContext context
    ) => await context.Database.ExecuteSqlRawAsync(
        $"DROP TABLE IF EXISTS `{CharTableName}`;",
        CancellationToken.None);

    private static async Task DropSqlModeTableAsync(
        SqlModeStringCollectionContext context
    ) => await context.Database.ExecuteSqlRawAsync(
        $"DROP TABLE IF EXISTS `{SqlModeTableName}`;",
        CancellationToken.None);

    private static async Task<string> ReadSqlModeAsync(
        DbConnection connection
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT @@SESSION.sql_mode;";

        return Assert.IsType<string>(
            await command.ExecuteScalarAsync(CancellationToken.None));
    }

    private static async Task SetSqlModeAsync(
        DbConnection connection,
        string sqlMode
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SET SESSION sql_mode = @sqlMode;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@sqlMode";
        parameter.Value = sqlMode;
        command.Parameters.Add(parameter);

        _ = await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task<int> ReadStringDecodeCoercibilityAsync(
        PrimitiveCollectionContext context
    )
    {
        await context.Database.OpenConnectionAsync(CancellationToken.None);

        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                SELECT COERCIBILITY(
                    JSON_UNQUOTE(JSON_QUOTE(CONVERT(FROM_BASE64('YQ==') USING utf8mb4))));
                """;

            return Convert.ToInt32(
                await command.ExecuteScalarAsync(CancellationToken.None),
                CultureInfo.InvariantCulture);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }


    private sealed class PrimitiveCollectionContext(
        DbContextOptions<PrimitiveCollectionContext> options
    ) : DbContext(options)
    {
        public DbSet<PrimitiveCollectionEntity> Items => Set<PrimitiveCollectionEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<PrimitiveCollectionEntity>(entity =>
            {
                entity.ToTable(TableName);
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Id)
                    .ValueGeneratedNever();
                entity
                    .Property(item => item.BinaryId)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Binary16);
                entity
                    .Property(item => item.OptionalBinaryId)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Binary16);
                entity
                    .Property(item => item.OptionalCharId)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Char36);
                entity
                    .Property(item => item.CharId)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Char36);
                entity
                    .Property(item => item.Name)
                    .HasMaxLength(64)
                    .UseCollation("utf8mb4_bin");
                entity
                    .Property(item => item.OptionalName)
                    .HasMaxLength(64)
                    .UseCollation("utf8mb4_bin");
                entity
                    .Property(item => item.ConvertedName)
                    .HasConversion(
                        value => "db-" + value,
                        value => value.Substring(3))
                    .HasMaxLength(64)
                    .UseCollation("utf8mb4_bin");
                entity
                    .Property(item => item.CaseInsensitiveName)
                    .HasMaxLength(64)
                    .UseCollation("utf8mb4_unicode_ci");
                entity
                    .Property(item => item.ConvertedText)
                    .HasConversion(
                        value => value.Value,
                        value => new TextValue(value))
                    .HasMaxLength(64)
                    .UseCollation("utf8mb4_bin");
                entity
                    .Property(item => item.Status)
                    .HasConversion<string>()
                    .HasMaxLength(16)
                    .UseCollation("utf8mb4_bin");
                entity
                    .Property(item => item.OptionalStatus)
                    .HasConversion<string>()
                    .HasMaxLength(16)
                    .UseCollation("utf8mb4_bin");
                entity
                    .Property(item => item.ConvertedPrice)
                    .HasConversion(
                        value => value.Value,
                        value => new PriceValue(value))
                    .HasPrecision(10, 1);
                entity
                    .Property(item => item.ConvertedGuid)
                    .HasConversion(
                        value => value.Value,
                        value => new StrongGuid(value))
                    .HasColumnType("binary(16)");
                entity
                    .Property(item => item.ConvertedDuration)
                    .HasConversion(
                        value => value.Value,
                        value => new ElapsedValue(value))
                    .HasPrecision(0);
                entity
                    .Property(item => item.ConvertedTimestamp)
                    .HasConversion(
                        value => value.UtcDateTime,
                        value => new DateTimeOffset(value, TimeSpan.Zero))
                    .HasPrecision(0);
                entity
                    .Property(item => item.ConvertedPayload)
                    .HasConversion(
                        value => Encoding.UTF8.GetBytes(value.Value),
                        value => new BinaryValue(Encoding.UTF8.GetString(value)))
                    .HasColumnType("varbinary(32)");
                entity
                    .Property(item => item.Payload)
                    .HasColumnType("varbinary(32)");
                entity
                    .PrimitiveCollection(item => item.GuidValues)
                    .HasColumnType("json");
            });
        }
    }

    private sealed class CharGuidCollectionContext(
        DbContextOptions<CharGuidCollectionContext> options
    ) : DbContext(options)
    {
        public DbSet<CharGuidCollectionEntity> Items => Set<CharGuidCollectionEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<CharGuidCollectionEntity>(entity =>
            {
                entity.ToTable(CharTableName);
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Id)
                    .ValueGeneratedNever();
                entity
                    .PrimitiveCollection(item => item.GuidValues)
                    .HasColumnType("json");
            });
        }
    }

    private sealed class SqlModeStringCollectionContext(
        DbContextOptions<SqlModeStringCollectionContext> options
    ) : DbContext(options)
    {
        public DbSet<SqlModeStringCollectionEntity> Items => Set<SqlModeStringCollectionEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<SqlModeStringCollectionEntity>(entity =>
            {
                entity.ToTable(SqlModeTableName);
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Id)
                    .ValueGeneratedNever();
                entity
                    .Property(item => item.Value)
                    .HasMaxLength(64)
                    .UseCollation("utf8mb4_bin");
                entity
                    .Property(item => item.OptionalValue)
                    .HasMaxLength(64)
                    .UseCollation("utf8mb4_bin");
                entity
                    .Property(item => item.ConvertedValue)
                    .HasConversion(
                        value => "db-" + value,
                        value => value.Substring(3))
                    .HasMaxLength(64)
                    .UseCollation("utf8mb4_bin");
                entity
                    .Property(item => item.ConvertedText)
                    .HasConversion(
                        value => value.Value,
                        value => new TextValue(value))
                    .HasMaxLength(64)
                    .UseCollation("utf8mb4_bin");
                entity
                    .PrimitiveCollection(item => item.Values)
                    .HasColumnType("json");
            });
        }
    }

    private sealed class PrimitiveCollectionEntity
    {
        public int Id { get; set; }

        public Guid BinaryId { get; set; }

        public Guid? OptionalBinaryId { get; set; }

        public Guid? OptionalCharId { get; set; }

        public Guid CharId { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? OptionalName { get; set; }

        public string ConvertedName { get; set; } = string.Empty;

        public string CaseInsensitiveName { get; set; } = string.Empty;

        public TextValue ConvertedText { get; set; }

        public CollectionStatus Status { get; set; }

        public CollectionStatus? OptionalStatus { get; set; }

        public PriceValue ConvertedPrice { get; set; }

        public StrongGuid ConvertedGuid { get; set; }

        public ElapsedValue ConvertedDuration { get; set; }

        public DateTimeOffset ConvertedTimestamp { get; set; }

        public BinaryValue ConvertedPayload { get; set; }

        public byte[] Payload { get; set; } = [];

        public Guid[] GuidValues { get; set; } = [];
    }

    private enum CollectionStatus
    {
        Inactive,
        Active,
    }

    private readonly record struct PriceValue(decimal Value);

    private readonly record struct StrongGuid(Guid Value);

    private readonly record struct ElapsedValue(TimeSpan Value);

    private readonly record struct TextValue(string Value);

    private readonly record struct BinaryValue(string Value);

    private sealed class CharGuidCollectionEntity
    {
        public int Id { get; set; }

        public Guid[] GuidValues { get; set; } = [];
    }

    private sealed class SqlModeStringCollectionEntity
    {
        public int Id { get; set; }

        public string Value { get; set; } = string.Empty;

        public string? OptionalValue { get; set; }

        public string ConvertedValue { get; set; } = string.Empty;

        public TextValue ConvertedText { get; set; }

        public string?[] Values { get; set; } = [];
    }
}
