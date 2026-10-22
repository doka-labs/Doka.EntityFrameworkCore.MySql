namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlGuidTextIntegrationTests
{
    private static readonly string[] s_unboundOperands =
        ["RequiredParameter", "NullableParameter", "NullParameter", "Constant"];

    private static readonly string[] s_storedTextPatterns = ["%AABBCCDDEEFF", "%aabbccddeeff"];
    private static readonly int[] s_matchingIds = [1];
    private static readonly int[] s_allIds = [1, 2];

    /// <summary>
    /// Identifies unbound constants, nullable parameters, and required parameters.
    /// </summary>
    public static IEnumerable<object[]> UnboundCases =>
        from target in SelectedTargets()
        from format in s_defaultFormats
        from operand in s_unboundOperands
        select new object[] { target.ToString(), format, operand };

    /// <summary>
    /// Identifies uppercase and lowercase patterns against binary-collated stored GUID text.
    /// </summary>
    public static IEnumerable<object[]> StoredTextCases =>
        from target in SelectedTargets()
        from format in s_defaultFormats
        from pattern in s_storedTextPatterns
        select new object[] { target.ToString(), format, pattern };

    /// <summary>
    /// Verifies an unbound operand independently of property mapping.
    /// </summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(UnboundCases))]
    public async Task Unbound_guid_like_preserves_parameter_and_constant_semantics(
        string target,
        MySqlGuidFormat defaultFormat,
        string operand
    )
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync(
            Enum.Parse<IntegrationDatabaseTarget>(target),
            defaultFormat);

        var token = s_matchingGuid;
        Guid? optionalToken = operand == "NullParameter" ? null : token;
        var query = operand switch
        {
            "RequiredParameter" => context.Set<GuidItem>().Where(item => EF.Functions.Like(token, item.Pattern)),
            "NullableParameter" or "NullParameter" => context.Set<GuidItem>()
                .Where(item => EF.Functions.Like(optionalToken, item.Pattern)),
            "Constant" => context.Set<GuidItem>()
                .Where(item => EF.Functions.Like(new Guid(MatchingText), item.Pattern)),
            _ => throw new ArgumentOutOfRangeException(nameof(operand)),
        };

        // Act
        var actual = await query.OrderBy(item => item.Id).Select(item => item.Id).ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(operand == "NullParameter" ? [] : s_allIds, actual);
    }

    /// <summary>
    /// Verifies canonical projection even when the stored CHAR value uses uppercase text.
    /// </summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(FormatCases))]
    public async Task Uppercase_stored_guid_projects_as_canonical_text(
        string target,
        MySqlGuidFormat defaultFormat
    )
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync(
            Enum.Parse<IntegrationDatabaseTarget>(target),
            defaultFormat);

        var query = context.Set<GuidItem>()
            .Where(item => item.Id == 1)
            .Select(item => EF.Property<Guid>(item, UppercasePropertyName).ToString());

        // Act
        var actual = await query.SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(MatchingText, actual);
    }

    /// <summary>
    /// Preserves stored-text collation instead of silently normalizing the LIKE operand.
    /// </summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(StoredTextCases))]
    public async Task Stored_guid_like_respects_binary_collation(
        string target,
        MySqlGuidFormat defaultFormat,
        string pattern
    )
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync(
            Enum.Parse<IntegrationDatabaseTarget>(target),
            defaultFormat);

        var query = context.Set<GuidItem>()
            .Where(item => EF.Functions.Like(EF.Property<Guid>(item, UppercasePropertyName), pattern));

        // Act
        var actual = await query.OrderBy(item => item.Id).Select(item => item.Id).ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(pattern == "%AABBCCDDEEFF" ? s_matchingIds : [], actual);
    }
}
