namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Verifies independently materializable JSON collection membership on every supported engine.</summary>
[IntegrationDatabaseTest]
public sealed partial class MySqlIndependentCollectionMembershipIntegrationTests :
    IClassFixture<MySqlIndependentCollectionMembershipIntegrationTests.SeededDatabaseFixture>
{
    private const string TableName = "IntIndependentCollectionMembership";
    private const int CorpusRowCount = 14000;
    private const int MembershipCount = 10000;
    private readonly SeededDatabaseFixture _fixture;

    /// <summary>Creates a case over the class-owned, lazily seeded corpus.</summary>
    /// <param name="fixture">The per-engine seed and cleanup owner.</param>
    public MySqlIndependentCollectionMembershipIntegrationTests(
        SeededDatabaseFixture fixture
    )
    {
        _fixture = fixture;
    }

    /// <summary>Retains an independent indexed rowset for the original 14,000/10,000 GUID query.</summary>
    /// <param name="targetName">The selected live engine.</param>
    /// <param name="format">The explicitly mapped GUID storage format.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(LargeGuidCases))]
    public async Task Large_guid_parameter_membership_retains_an_independent_rowset(
        string targetName,
        MySqlGuidFormat format
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target, CancellationToken.None);
        await using var context = CreateContext(target);
        var values = Enumerable
            .Range(0, MembershipCount)
            .Select(CreateGuid)
            .ToArray();

        var query = GuidMembershipQuery(context.Items.AsNoTracking(), format, values, false);

        // Act
        var sql = query.ToQueryString();
        var count = await query.CountAsync(CancellationToken.None);

        // Assert
        Assert.Equal(MembershipCount, count);
        Assert.Contains(" IN (", sql, StringComparison.Ordinal);
        Assert.Contains("JSON_TABLE", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("COUNT(*)", sql, StringComparison.Ordinal);
    }

    /// <summary>Preserves GUID membership results, including null compensation and native UUID readback.</summary>
    /// <param name="targetName">The selected live engine.</param>
    /// <param name="format">The explicitly mapped GUID storage format.</param>
    /// <param name="scenario">The independently reported membership scenario.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(GuidCases))]
    public async Task Guid_parameter_membership_preserves_set_semantics(
        string targetName,
        MySqlGuidFormat format,
        string scenario
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target, CancellationToken.None);
        await using var context = CreateContext(target);
        var (values, nullable, negated, expected) = GuidScenario(scenario);
        var source = context
            .Items
            .AsNoTracking()
            .Where(row => row.Id <= 4);

        var query = nullable
            ? NullableGuidMembershipQuery(source, format, values, negated)
            : GuidMembershipQuery(
                source,
                format,
                values
                    .Select(value => value
                        ?? throw new InvalidOperationException("Non-nullable membership data contains null."))
                    .ToArray(),
                negated);

        // Act
        var actual = await SelectIdsAsync(query, CancellationToken.None);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>Reads actual GUID values back from native UUID columns after JSON membership.</summary>
    /// <param name="targetName">The selected MariaDB engine.</param>
    [RequiresDatabaseTargetsTheory(
        new[]
        {
            IntegrationDatabaseTarget.MariaDb1011,
            IntegrationDatabaseTarget.MariaDb114,
            IntegrationDatabaseTarget.MariaDb118,
            IntegrationDatabaseTarget.MariaDb123,
        })]
    [MemberData(nameof(NativeUuidCases))]
    public async Task Native_uuid_membership_round_trips_selected_guid_values(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target, CancellationToken.None);
        await using var context = CreateContext(target);
        Guid[] values = [CreateGuid(0), CreateGuid(2)];
        var query = GuidMembershipQuery(context.Items.AsNoTracking(), MySqlGuidFormat.Uuid, values, false);

        // Act
        var actual = await query
            .OrderBy(row => row.Id)
            .Select(row => row.NativeId)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(values, actual);
    }

    /// <summary>Retains decoded text and explicit nullable membership semantics.</summary>
    /// <param name="targetName">The selected live engine.</param>
    /// <param name="scenario">The independently reported membership scenario.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(TextCases))]
    public async Task String_parameter_membership_preserves_decoded_values(
        string targetName,
        string scenario
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target, CancellationToken.None);
        await using var context = CreateContext(target);
        var (values, negated, expected) = TextScenario(scenario);
        var source = context
            .Items
            .AsNoTracking()
            .Where(row => row.Id <= 4);

        var query = negated
            ? source.Where(row => !EF
                .Parameter(values)
                .Contains(row.Name))
            : source.Where(row => EF
                .Parameter(values)
                .Contains(row.Name));

        // Act
        var actual = await SelectIdsAsync(query, CancellationToken.None);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>Retains the provider string representation of nullable enum values.</summary>
    /// <param name="targetName">The selected live engine.</param>
    /// <param name="scenario">The independently reported membership scenario.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(TextCases))]
    public async Task Converted_enum_parameter_membership_preserves_model_values(
        string targetName,
        string scenario
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target, CancellationToken.None);
        await using var context = CreateContext(target);
        var (values, negated, expected) = EnumScenario(scenario);
        var source = context
            .Items
            .AsNoTracking()
            .Where(row => row.Id <= 4);

        var query = negated
            ? source.Where(row => !EF
                .Parameter(values)
                .Contains(row.Status))
            : source.Where(row => EF
                .Parameter(values)
                .Contains(row.Status));

        // Act
        var actual = await SelectIdsAsync(query, CancellationToken.None);

        // Assert
        Assert.Equal(expected, actual);
    }
}
