namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Protects independent JSON membership and the existing correlated-query boundaries.</summary>
public sealed partial class MySqlIndependentCollectionMembershipSqlTests
{
    /// <summary>Supplies independently reported root, path, decoder and negation cases on both engines.</summary>
    public static IEnumerable<TheoryDataRow<bool, string, bool, string, bool>> IndependentCases
    {
        get
        {
            // WHY: Nested yields keep each case dimension explicit without materializing the Cartesian product.
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var mariaDb in new[] { false, true })
            {
                foreach (var root in new[] { "parameter", "constant" })
                {
                    foreach (var nullPath in new[] { false, true })
                    {
                        foreach (var decoder in new[] { "binary", "char", "string", "enum" })
                        {
                            foreach (var negated in new[] { false, true })
                            {
                                yield return new TheoryDataRow<bool, string, bool, string, bool>(mariaDb,
                                    root,
                                    nullPath,
                                    decoder,
                                    negated);
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>Supplies correlation and composition cases without hiding a failure inside a test loop.</summary>
    public static IEnumerable<TheoryDataRow<bool, string, string, bool>> ProtectedCases
    {
        get
        {
            foreach (var mariaDb in new[] { false, true })
            {
                yield return
                    new TheoryDataRow<bool, string, string, bool>(mariaDb, "static-path", "'$.items[*]'", true);

                yield return new TheoryDataRow<bool, string, string, bool>(mariaDb,
                    "outer-index-path",
                    "`e`.`Index`",
                    true);

                yield return new TheoryDataRow<bool, string, string, bool>(mariaDb,
                    "entity-root",
                    "JSON_TABLE(`e`.`Document`",
                    true);

                yield return new TheoryDataRow<bool, string, string, bool>(mariaDb,
                    "wrapped-root",
                    "JSON_EXTRACT(@values, '$')",
                    true);

                yield return new TheoryDataRow<bool, string, string, bool>(mariaDb,
                    "predicate",
                    "`j`.`value` IS NOT NULL",
                    true);

                yield return new TheoryDataRow<bool, string, string, bool>(mariaDb,
                    "natural-order",
                    "SELECT COUNT(*)",
                    true);

                yield return
                    new TheoryDataRow<bool, string, string, bool>(mariaDb, "limit", "`j`.`ordinal` <= 2", true);

                yield return
                    new TheoryDataRow<bool, string, string, bool>(mariaDb, "offset", "`j`.`ordinal` > 1", true);

                yield return new TheoryDataRow<bool, string, string, bool>(mariaDb,
                    "distinct",
                    "SELECT DISTINCT",
                    false);

                yield return new TheoryDataRow<bool, string, string, bool>(mariaDb,
                    "reverse-order",
                    "ORDER BY `j`.`ordinal` DESC",
                    false);

                yield return new TheoryDataRow<bool, string, string, bool>(mariaDb, "group", "GROUP BY", false);
                yield return new TheoryDataRow<bool, string, string, bool>(mariaDb, "having", "HAVING", false);
                yield return new TheoryDataRow<bool, string, string, bool>(mariaDb,
                    "predicate-limit",
                    "`__doka_isolated_subquery`",
                    false);
            }
        }
    }

    /// <summary>Independent decoded rowsets retain their original IN expression and decoder.</summary>
    /// <param name="mariaDb">Whether the MariaDB SQL profile is used.</param>
    /// <param name="root">The independent JSON argument kind.</param>
    /// <param name="nullPath">Whether the JSON row path is null instead of empty.</param>
    /// <param name="decoder">The provider representation to decode.</param>
    /// <param name="negated">Whether membership is negated.</param>
    [Theory]
    [MemberData(nameof(IndependentCases))]
    public void Independent_decoded_membership_remains_an_in_subquery(
        bool mariaDb,
        string root,
        bool nullPath,
        string decoder,
        bool negated
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = CreateMembership(context, root, nullPath, decoder, "none", negated);

        // Act
        var actual = Generate(context, query);

        // Assert
        Assert.Contains(negated
                ? " NOT IN ("
                : " IN (",
            actual,
            StringComparison.Ordinal);

        Assert.Contains("JSON_TABLE(", actual, StringComparison.Ordinal);
        Assert.Contains(
            decoder switch
            {
                "binary" => "UNHEX(REPLACE(`j`.`value`, '-', ''))",
                "char" => "JSON_UNQUOTE(JSON_QUOTE(`j`.`value`))",
                _ => "FROM_BASE64(`j`.`value`)",
            },
            actual,
            StringComparison.Ordinal);

        Assert.DoesNotContain("SELECT COUNT(*)", actual, StringComparison.Ordinal);
    }

    /// <summary>Correlated and composed rowsets retain the safeguards selected by the existing generator.</summary>
    /// <param name="mariaDb">Whether the MariaDB SQL profile is used.</param>
    /// <param name="scenario">The correlation or composition boundary.</param>
    /// <param name="expected">The SQL fragment proving the existing boundary survived.</param>
    /// <param name="scalarCount">Whether the existing scalar COUNT workaround applies.</param>
    [Theory]
    [MemberData(nameof(ProtectedCases))]
    public void Correlated_or_composed_membership_keeps_its_existing_boundary(
        bool mariaDb,
        string scenario,
        string expected,
        bool scalarCount
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = CreateMembership(context, "parameter", false, "string", scenario, false);

        // Act
        var actual = Generate(context, query);

        // Assert
        Assert.Contains(expected, actual, StringComparison.Ordinal);
        Assert.Equal(scalarCount, actual.Contains("SELECT COUNT(*)", StringComparison.Ordinal));
        Assert.Contains("FROM_BASE64(`j`.`value`)", actual, StringComparison.Ordinal);
    }

    /// <summary>Unencoded membership selects the native or escape-safe path for the engine.</summary>
    /// <param name="mariaDb">Whether the MariaDB SQL profile is used.</param>
    /// <param name="root">The independent JSON argument kind.</param>
    /// <param name="wrapped">Whether native text uses the JSON_UNQUOTE/JSON_QUOTE projection wrapper.</param>
    [Theory]
    [InlineData(false, "parameter", false)]
    [InlineData(false, "constant", false)]
    [InlineData(true, "parameter", false)]
    [InlineData(true, "constant", false)]
    [InlineData(false, "parameter", true)]
    [InlineData(false, "constant", true)]
    [InlineData(true, "parameter", true)]
    [InlineData(true, "constant", true)]
    public void Native_membership_uses_the_engine_qualified_path(
        bool mariaDb,
        string root,
        bool wrapped
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // WHY: The native wrapper also matches the independent fallback, so it pins the fast-path ordering.
        var query = CreateMembership(context,
            root,
            false,
            wrapped
                ? "native-wrapper"
                : "native",
            "none",
            false);

        // Act
        var actual = Generate(context, query);

        // Assert
        // WHY: This fixture targets MariaDB 12.3.0, before the native escape-comparison fix.
        Assert.Equal(!mariaDb, actual.Contains("JSON_CONTAINS(", StringComparison.Ordinal));
        Assert.Equal(mariaDb, actual.Contains("JSON_TABLE(", StringComparison.Ordinal));

        if (mariaDb)
        {
            Assert.Contains("CAST(JSON_UNQUOTE(`j`.`value`) AS BINARY)", actual, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("SELECT COUNT(*)", actual, StringComparison.Ordinal);
    }
}
