namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies scalar LIKE translation and execution against every supported engine.
/// </summary>
[IntegrationDatabaseTest]
public sealed partial class MySqlLikeIntegrationTests : IClassFixture<MySqlLikeIntegrationTests.LikeFixture>
{
    private readonly LikeFixture _fixture;
    private static readonly string[] s_guidParameterScenarios = ["Required", "Nullable", "Null"];
    private static readonly string[] s_stringEscapeOverloads = ["Generic", "Standard", "Nullable"];
    private static readonly int[] s_matchingIds = [1];

    /// <summary>
    /// Uses the read-only scalar dataset shared by this class's independent cases.
    /// </summary>
    public MySqlLikeIntegrationTests(
        LikeFixture fixture
    )
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Identifies each selected engine, scalar property, and matching scenario.
    /// </summary>
    public static IEnumerable<object[]> ScalarLikeCases =>
        from target in SelectedTargets()
        from testCase in s_scalarCases
        select new object[] { target.ToString(), testCase.PropertyName, testCase.Name };

    /// <summary>
    /// Identifies the selected supported engines.
    /// </summary>
    public static IEnumerable<object[]> TargetCases =>
        SelectedTargets().Select(target => new object[] { target.ToString() });

    /// <summary>
    /// Identifies nullable and non-nullable GUID parameters independently.
    /// </summary>
    public static IEnumerable<object[]> GuidParameterCases =>
        from target in SelectedTargets()
        from scenario in s_guidParameterScenarios
        select new object[] { target.ToString(), scenario };

    /// <summary>
    /// Identifies string overloads whose escape semantics are executed.
    /// </summary>
    public static IEnumerable<object[]> StringEscapeCases =>
        from target in SelectedTargets()
        from overload in s_stringEscapeOverloads
        select new object[] { target.ToString(), overload };

    /// <summary>
    /// Verifies one scalar type, matching boundary, and its SQL representation.
    /// </summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(ScalarLikeCases))]
    public async Task Scalar_like_preserves_value_and_sql_contract(
        string target,
        string propertyName,
        string scenario
    )
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync(Enum.Parse<IntegrationDatabaseTarget>(target));
        var testCase = s_scalarCases.Single(item => item.PropertyName == propertyName && item.Name == scenario);
        var query = CreateLikeQuery(context, testCase);
        var sql = query.ToQueryString();

        // Act
        var actual = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(testCase.ExpectedIds, actual);

        if (!testCase.HasEscape)
        {
            Assert.InRange(sql.Length, 1, 1024);
        }

        if (testCase.Pattern is not null && (!testCase.HasEscape || testCase.EscapeCharacter is not null))
        {
            Assert.Contains(" LIKE @pattern", sql, StringComparison.Ordinal);

            if (testCase.HasEscape)
            {
                Assert.Contains("ESCAPE @escapeCharacter", sql, StringComparison.Ordinal);
            }
            else
            {
                Assert.DoesNotContain("CAST(", sql, StringComparison.OrdinalIgnoreCase);

                if (propertyName.Contains("BinaryToken", StringComparison.Ordinal))
                {
                    Assert.Contains($"HEX(`i`.`{propertyName}`)", sql, StringComparison.OrdinalIgnoreCase);
                    Assert.Contains("LOWER(CONCAT(", sql, StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    Assert.Contains($"`{propertyName}` LIKE @pattern", sql, StringComparison.Ordinal);
                    Assert.DoesNotContain("HEX(", sql, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    /// <summary>
    /// Verifies equivalence between scalar and explicitly formatted GUID LIKE operands.
    /// </summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(TargetCases))]
    public async Task Scalar_and_formatted_guid_like_have_equivalent_results(
        string target
    )
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync(Enum.Parse<IntegrationDatabaseTarget>(target));
        var pattern = "00112233-%-aabbccddeeff";
        var formattedQuery = context.Items
            .Where(item => EF.Functions.Like(item.BinaryToken.ToString(), pattern))
            .Select(item => item.Id);
        var scalarQuery = context.Items
            .Where(item => EF.Functions.Like(item.BinaryToken, pattern))
            .Select(item => item.Id);

        // Act
        var formattedMatches = await formattedQuery.ToArrayAsync(CancellationToken.None);
        var scalarMatches = await scalarQuery.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([1], formattedMatches);
        Assert.Equal(formattedMatches, scalarMatches);
    }

    /// <summary>
    /// Verifies canonical text projection independently of GUID filtering.
    /// </summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(TargetCases))]
    public async Task Binary_guid_projection_uses_canonical_text(
        string target
    )
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync(Enum.Parse<IntegrationDatabaseTarget>(target));
        var query = context.Items.Where(item => item.Id == 1).Select(item => item.BinaryToken.ToString());

        // Act
        var actual = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([s_matchingGuid.ToString()], actual);
    }

    /// <summary>
    /// Verifies required, nullable, and null GUID parameters against a pattern column.
    /// </summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(GuidParameterCases))]
    public async Task Unbound_guid_parameter_preserves_like_null_semantics(
        string target,
        string scenario
    )
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync(Enum.Parse<IntegrationDatabaseTarget>(target));
        var token = s_matchingGuid;
        Guid? optionalToken = scenario == "Null" ? null : token;
        var query = scenario == "Required"
            ? context.Items.Where(item => EF.Functions.Like(token, item.Pattern))
            : context.Items.Where(item => EF.Functions.Like(optionalToken, item.Pattern));

        // Act
        var actual = await query.Select(item => item.Id).ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(scenario == "Null" ? [] : s_matchingIds, actual);
    }

    /// <summary>
    /// Verifies SQL parity between provider scalar and standard string overloads.
    /// </summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(TargetCases))]
    public async Task Generic_and_standard_string_like_overloads_produce_identical_sql(
        string target
    )
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync(Enum.Parse<IntegrationDatabaseTarget>(target));
        var escapedPattern = "sale!_25!%!!";
        var escapeCharacter = "!";
        var genericQuery = context.Items.Where(item =>
            EF.Functions.Like<string>(item.Text, escapedPattern, escapeCharacter));
        var standardQuery = context.Items.Where(item =>
            DbFunctionsExtensions.Like(EF.Functions, item.Text, escapedPattern, escapeCharacter));

        // Act
        var genericSql = genericQuery.ToQueryString();
        var standardSql = standardQuery.ToQueryString();

        // Assert
        Assert.Equal(genericSql, standardSql);
    }

    /// <summary>
    /// Verifies escaped percent, underscore, and escape characters as literal data.
    /// </summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(StringEscapeCases))]
    public async Task Escaped_string_like_matches_literal_wildcards(
        string target,
        string overload
    )
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync(Enum.Parse<IntegrationDatabaseTarget>(target));
        var escapedPattern = "sale!_25!%!!";
        var escapeCharacter = "!";
        var query = overload switch
        {
            "Generic" => context.Items.Where(item =>
                EF.Functions.Like<string>(item.Text, escapedPattern, escapeCharacter)),
            "Standard" => context.Items.Where(item =>
                DbFunctionsExtensions.Like(EF.Functions, item.Text, escapedPattern, escapeCharacter)),
            "Nullable" => context.Items.Where(item =>
                item.OptionalText != null && EF.Functions.Like(item.OptionalText, escapedPattern, "!")),
            _ => throw new ArgumentOutOfRangeException(nameof(overload)),
        };

        // Act
        var actual = await query.Select(item => item.Id).ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([1], actual);
    }
}
