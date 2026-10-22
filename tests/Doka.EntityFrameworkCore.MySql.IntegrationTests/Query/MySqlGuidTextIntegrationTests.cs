namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies GUID formatting, matching, and generated values against stored
/// representations on every supported database engine.
/// </summary>
[IntegrationDatabaseTest]
public sealed partial class MySqlGuidTextIntegrationTests : IClassFixture<MySqlGuidTextIntegrationTests.GuidFixture>
{
    private readonly GuidFixture _fixture;
    private static readonly string[] s_matchingOperations =
    [
        "TextEqual", "TextNotEqual", "LikeMatch", "LikeNoMatch",
        "OptionalLikeMatch", "OptionalWildcard", "OptionalNoMatch",
        "OptionalNullPattern", "OptionalEscaped",
    ];

    /// <summary>
    /// Uses the per-format immutable datasets shared by this class's independent cases.
    /// </summary>
    public MySqlGuidTextIntegrationTests(
        GuidFixture fixture
    )
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Identifies each configured default and explicit property mapping.
    /// </summary>
    public static IEnumerable<object[]> PropertyCases =>
        from target in SelectedTargets()
        from format in s_defaultFormats
        from property in s_properties
        select new object[] { target.ToString(), format, property };

    /// <summary>
    /// Identifies each matching operation independently of other operations.
    /// </summary>
    public static IEnumerable<object[]> MatchingCases =>
        from target in SelectedTargets()
        from format in s_defaultFormats
        from property in s_properties
        from operation in s_matchingOperations
        select new object[] { target.ToString(), format, property, operation };

    /// <summary>
    /// Identifies the selected engine and default-format pairs.
    /// </summary>
    public static IEnumerable<object[]> FormatCases =>
        from target in SelectedTargets()
        from format in s_defaultFormats
        select new object[] { target.ToString(), format };

    /// <summary>
    /// Verifies canonical projection for one property mapping and configured default.
    /// </summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(PropertyCases))]
    public async Task Guid_projection_uses_canonical_text(
        string target,
        MySqlGuidFormat defaultFormat,
        string propertyName
    )
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync(
            Enum.Parse<IntegrationDatabaseTarget>(target),
            defaultFormat);

        var query = context.Set<GuidItem>()
            .OrderBy(item => item.Id)
            .Select(item => EF.Property<Guid>(item, propertyName).ToString());

        // Act
        var actual = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([MatchingText, Guid.Empty.ToString()], actual);
    }

    /// <summary>
    /// Verifies one predicate operation without hiding later failures behind earlier queries.
    /// </summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(MatchingCases))]
    public async Task Guid_text_predicate_preserves_matching_and_null_semantics(
        string target,
        MySqlGuidFormat defaultFormat,
        string propertyName,
        string operation
    )
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync(
            Enum.Parse<IntegrationDatabaseTarget>(target),
            defaultFormat);

        var query = CreateMatchingQuery(context, propertyName, operation);
        var expected = operation switch
        {
            "TextNotEqual" => new[] { 2 },
            "LikeNoMatch" or "OptionalNoMatch" or "OptionalNullPattern" => [],
            _ => [1],
        };

        // Act
        var actual = await query.OrderBy(item => item.Id).Select(item => item.Id).ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Verifies that server-generated GUIDs are nonempty and distinct per row.
    /// </summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(FormatCases))]
    public async Task Generated_guids_are_distinct_and_nonempty(
        string target,
        MySqlGuidFormat defaultFormat
    )
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync(
            Enum.Parse<IntegrationDatabaseTarget>(target),
            defaultFormat);

        var query = context.Set<GuidItem>().Select(item => Guid.NewGuid());

        // Act
        var actual = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, actual.Length);
        Assert.All(actual, value => Assert.NotEqual(Guid.Empty, value));
        Assert.NotEqual(actual[0], actual[1]);
    }

    /// <summary>
    /// Verifies canonical server-generated GUID text independently of GUID materialization.
    /// </summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(FormatCases))]
    public async Task Generated_guid_text_is_canonical_and_nonempty(
        string target,
        MySqlGuidFormat defaultFormat
    )
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync(
            Enum.Parse<IntegrationDatabaseTarget>(target),
            defaultFormat);

        var query = context.Set<GuidItem>().Select(item => Guid.NewGuid().ToString());

        // Act
        var actual = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, actual.Length);
        Assert.All(actual, value =>
        {
            Assert.True(Guid.TryParseExact(value, "D", out var guid));
            Assert.NotEqual(Guid.Empty, guid);
            Assert.Equal(guid.ToString(), value);
        });
    }

    private static IQueryable<GuidItem> CreateMatchingQuery(
        GuidContext context,
        string propertyName,
        string operation
    )
    {
        var optionalName = $"Optional{propertyName}";
        var pattern = operation switch
        {
            "LikeNoMatch" or "OptionalNoMatch" => "ffffffff-%",
            "OptionalWildcard" => "%",
            "OptionalNullPattern" => null,
            "OptionalEscaped" => "00112233-%",
            _ => "00112233-%-aabbccddeeff",
        };

        return operation switch
        {
            "TextEqual" => context.Set<GuidItem>()
                .Where(item => EF.Property<Guid>(item, propertyName).ToString() == MatchingText),
            "TextNotEqual" => context.Set<GuidItem>()
                .Where(item => EF.Property<Guid>(item, propertyName).ToString() != MatchingText),
            "LikeMatch" or "LikeNoMatch" => context.Set<GuidItem>()
                .Where(item => EF.Functions.Like(EF.Property<Guid>(item, propertyName), pattern)),
            "OptionalLikeMatch" or "OptionalWildcard" or "OptionalNoMatch" or "OptionalNullPattern" =>
                context.Set<GuidItem>()
                    .Where(item => EF.Functions.Like(EF.Property<Guid?>(item, optionalName), pattern)),
            "OptionalEscaped" => context.Set<GuidItem>()
                .Where(item => EF.Functions.Like(EF.Property<Guid?>(item, optionalName), "00112233-%", "!")),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
    }
}
