namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Verifies predicate grouping at the scalar JSON membership boundary.</summary>
public sealed partial class MySqlPrimitiveCollectionParameterTests
{
    private static readonly bool[] s_predicateGroupingEngineFamilies = [false, true];

    private static readonly string[] s_predicateGroupingScenarios =
    [
        "native-or-absent",
        "native-or-present",
        "native-or-negated-absent",
        "native-or-negated-present",
        "native-and-absent",
        "native-equality-or-absent",
        "native-null-match",
        "native-null-nonmatch",
        "native-null-negated-match",
        "native-null-negated-nonmatch",
        "decoded-inequality",
        "decoded-or",
        "decoded-or-negated",
    ];

    /// <summary>Reports each composition independently for both supported engine families.</summary>
    public static IEnumerable<object[]> PredicateGroupingCases => s_predicateGroupingEngineFamilies.SelectMany(mariaDb
        =>
        s_predicateGroupingScenarios.Select(scenario => new object[] { mariaDb, scenario }));

    /// <summary>Keeps every COUNT predicate together before appending null-safe membership.</summary>
    /// <param name="mariaDb">Whether to translate for MariaDB.</param>
    /// <param name="scenario">The independently reported composition boundary.</param>
    [Theory]
    [MemberData(nameof(PredicateGroupingCases))]
    public void Composed_membership_groups_count_predicate(
        bool mariaDb,
        string scenario
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb: mariaDb);
        var query = CreatePredicateGroupingQuery(context.Items, scenario);

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("JSON_TABLE(", sql, StringComparison.Ordinal);
        var count = sql.IndexOf("SELECT COUNT(*)", StringComparison.Ordinal);

        if (count >= 0)
        {
            var predicate = sql.IndexOf("WHERE ", count, StringComparison.Ordinal) + "WHERE ".Length;
            Assert.Equal('(', sql[predicate]);
            Assert.Contains(") AND ", sql[predicate..], StringComparison.Ordinal);
        }
        else
        {
            // WHY: EF may retain IN/EXISTS for nullable or decoded inputs;
            // the predicate-grouping fix must not force them into the COUNT path.
            Assert.True(
                scenario.Contains("null", StringComparison.Ordinal)
                || scenario.StartsWith("decoded-", StringComparison.Ordinal));
        }
    }

    /// <summary>Leaves the uncomposed native JSON_CONTAINS fast path intact.</summary>
    /// <param name="mariaDb">Whether to translate for MariaDB.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Uncomposed_native_membership_keeps_json_contains(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb: mariaDb);
        int[] numbers = [1, 2];
        var query = context.Items.Where(item => EF
            .Parameter(numbers)
            .Contains(item.Id));

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("JSON_CONTAINS(", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT COUNT(*)", sql, StringComparison.Ordinal);
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
