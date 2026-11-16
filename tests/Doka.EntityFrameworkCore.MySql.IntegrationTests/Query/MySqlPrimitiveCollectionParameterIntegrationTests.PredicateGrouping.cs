namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Verifies composed JSON membership results against immutable fixture rows.</summary>
public sealed partial class MySqlPrimitiveCollectionParameterIntegrationTests
{
    private static readonly (string Scenario, int[] Expected)[] s_predicateGroupingCases =
    [
        ("native-or-absent", Array.Empty<int>()),
        ("native-or-present", new[] { 2 }),
        ("native-or-negated-absent", new[] { 3 }),
        ("native-or-negated-present", Array.Empty<int>()),
        ("native-and-absent", Array.Empty<int>()),
        ("native-equality-or-absent", Array.Empty<int>()),
        ("native-null-match", new[] { 2 }),
        ("native-null-nonmatch", Array.Empty<int>()),
        ("native-null-negated-match", Array.Empty<int>()),
        ("native-null-negated-nonmatch", new[] { 3 }),
        ("decoded-inequality", new[] { 2 }),
        ("decoded-or", new[] { 1, 2 }),
        ("decoded-or-negated", new[] { 3 }),
    ];

    /// <summary>Reports every semantic boundary separately on every selected supported engine.</summary>
    public static IEnumerable<object[]> PredicateGroupingCases => SelectedTargets.SelectMany(target =>
        s_predicateGroupingCases.Select(test => new[] { target[0], test.Scenario, test.Expected }));

    /// <summary>Preserves the relationship between a filtered JSON value and its membership candidate.</summary>
    /// <param name="targetName">The selected database engine.</param>
    /// <param name="scenario">The independently reported semantic boundary.</param>
    /// <param name="expected">The exact fixture ids selected by the CLR membership expression.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(PredicateGroupingCases))]
    public async Task Composed_membership_preserves_predicate_grouping(
        string targetName,
        string scenario,
        int[] expected
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        await _fixture.EnsureSeededAsync(target, CancellationToken.None);
        await using var context = CreateContext(target);
        var query = CreatePredicateGroupingQuery(context.Items, scenario);

        // Act
        var actual = await SelectIdsAsync(query, CancellationToken.None);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>Builds the same predicate and membership domain without changing stored rows.</summary>
    private static IQueryable<PrimitiveCollectionEntity> CreatePredicateGroupingQuery(
        IQueryable<PrimitiveCollectionEntity> source,
        string scenario
    )
    {
        int[] numbers = [1, 2];
        int?[] nullable = [null, 1];
        string[] names = ["alpha", "\"quoted\""];
        source = source.Where(item => item.Id <= 3);

        // WHY: Mixed equality/range OR cannot be folded to IN. A first value
        // that passes OR but differs from the candidate exposes a missing group.
        return scenario switch
        {
            "native-or-absent" => source.Where(item => item.Id == 3
                && EF
                    .Parameter(numbers)
                    .Where(value => value == 1 || value > 1)
                    .Contains(item.Id)),
            "native-or-present" => source.Where(item => item.Id == 2
                && EF
                    .Parameter(numbers)
                    .Where(value => value == 1 || value > 1)
                    .Contains(item.Id)),
            "native-or-negated-absent" => source.Where(item => item.Id == 3
                && !EF
                    .Parameter(numbers)
                    .Where(value => value == 1 || value > 1)
                    .Contains(item.Id)),
            "native-or-negated-present" => source.Where(item => item.Id == 2
                && !EF
                    .Parameter(numbers)
                    .Where(value => value == 1 || value > 1)
                    .Contains(item.Id)),
            "native-and-absent" => source.Where(item => item.Id == 3
                && EF
                    .Parameter(numbers)
                    .Where(value => value > 0 && value < 3)
                    .Contains(item.Id)),
            "native-equality-or-absent" => source.Where(item => item.Id == 3
                && EF
                    .Parameter(numbers)
                    .Where(value => value == 1 || value == 2)
                    .Contains(item.Id)),
            "native-null-match" => source.Where(item => item.Id == 2
                && EF
                    .Parameter(nullable)
                    .Where(value => value == null || value > 0)
                    .Contains(
                        item.Id == 2
                            ? null
                            : item.Id)),
            "native-null-nonmatch" => source.Where(item => item.Id == 3
                && EF
                    .Parameter(nullable)
                    .Where(value => value == null || value > 0)
                    .Contains(
                        item.Id == 2
                            ? null
                            : item.Id)),
            "native-null-negated-match" => source.Where(item => item.Id == 2
                && !EF
                    .Parameter(nullable)
                    .Where(value => value == null || value > 0)
                    .Contains(
                        item.Id == 2
                            ? null
                            : item.Id)),
            "native-null-negated-nonmatch" => source.Where(item => item.Id == 3
                && !EF
                    .Parameter(nullable)
                    .Where(value => value == null || value > 0)
                    .Contains(
                        item.Id == 2
                            ? null
                            : item.Id)),
            "decoded-inequality" => source.Where(item => EF
                .Parameter(names)
                .Where(value => value != "alpha")
                .Contains(item.Name)),
            "decoded-or" => source.Where(item => EF
                .Parameter(names)
                .Where(value => value == "alpha" || value.Length > 3)
                .Contains(item.Name)),
            "decoded-or-negated" => source.Where(item => !EF
                .Parameter(names)
                .Where(value => value == "alpha" || value.Length > 3)
                .Contains(item.Name)),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
    }
}
