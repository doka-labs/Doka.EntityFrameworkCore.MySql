namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Protects independent decoded membership without changing correlated collection semantics.</summary>
public sealed partial class MySqlPrimitiveCollectionParameterTests
{
    /// <summary>Supplies one decoder-specific case per engine and model/provider representation.</summary>
    public static IEnumerable<TheoryDataRow<bool, string, string>> IndependentRepresentationCases
    {
        get
        {
            foreach (var mariaDb in new[] { false, true })
            {
                yield return new TheoryDataRow<bool, string, string>(mariaDb, "binary-guid", "UNHEX(REPLACE(");
                yield return new TheoryDataRow<bool, string, string>(mariaDb, "char-guid", "JSON_UNQUOTE(JSON_QUOTE(");
                yield return new TheoryDataRow<bool, string, string>(mariaDb, "string", "FROM_BASE64(");
                yield return new TheoryDataRow<bool, string, string>(mariaDb, "converted-string", "FROM_BASE64(");
                yield return new TheoryDataRow<bool, string, string>(mariaDb, "converted-enum", "FROM_BASE64(");
                yield return new TheoryDataRow<bool, string, string>(mariaDb, "converted-guid", "UNHEX(REPLACE(");
                yield return new TheoryDataRow<bool, string, string>(mariaDb, "bytes", "FROM_BASE64(");
                yield return new TheoryDataRow<bool, string, string>(mariaDb, "duration", "END AS TIME(6))");
            }
        }
    }

    /// <summary>Supplies independent empty, duplicate and negated inputs for both GUID formats.</summary>
    public static IEnumerable<TheoryDataRow<bool, bool, string>> IndependentGuidBoundaryCases
    {
        get
        {
            foreach (var mariaDb in new[] { false, true })
            {
                foreach (var char36 in new[] { false, true })
                {
                    yield return new TheoryDataRow<bool, bool, string>(mariaDb, char36, "empty");
                    yield return new TheoryDataRow<bool, bool, string>(mariaDb, char36, "duplicates");
                    yield return new TheoryDataRow<bool, bool, string>(mariaDb, char36, "negated");
                }
            }
        }
    }

    /// <summary>Retains the decoder and materializable IN shape for each independent provider representation.</summary>
    /// <param name="mariaDb">Whether the MariaDB profile is configured.</param>
    /// <param name="representation">The model/provider representation to query.</param>
    /// <param name="decoder">The SQL decoder that must remain in the projected rowset.</param>
    [Theory]
    [MemberData(nameof(IndependentRepresentationCases))]
    public void Independent_decoded_parameters_preserve_in_membership(
        bool mariaDb,
        string representation,
        string decoder
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb: mariaDb);
        var query = CreateIndependentMembershipQuery(context, representation);

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains(" IN (", sql, StringComparison.Ordinal);
        Assert.Contains("JSON_TABLE(@", sql, StringComparison.Ordinal);
        Assert.Contains(decoder, sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT COUNT(*)", sql, StringComparison.Ordinal);
    }

    /// <summary>Empty, duplicate and negated GUID membership retain the same independent rowset boundary.</summary>
    /// <param name="mariaDb">Whether the MariaDB profile is configured.</param>
    /// <param name="char36">Whether the compared property stores textual GUIDs.</param>
    /// <param name="scenario">The input cardinality or negation boundary.</param>
    [Theory]
    [MemberData(nameof(IndependentGuidBoundaryCases))]
    public void Independent_guid_boundaries_preserve_in_membership(
        bool mariaDb,
        bool char36,
        string scenario
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb: mariaDb);
        Guid[] values = scenario == "empty"
            ? []
            : [s_first, s_first, s_second];

        var query = (char36, scenario == "negated") switch
        {
            (false, false) => context.Items.Where(item => EF
                .Parameter(values)
                .Contains(item.BinaryId)),
            (false, true) => context.Items.Where(item => !EF
                .Parameter(values)
                .Contains(item.BinaryId)),
            (true, false) => context.Items.Where(item => EF
                .Parameter(values)
                .Contains(item.CharId)),
            (true, true) => context.Items.Where(item => !EF
                .Parameter(values)
                .Contains(item.CharId)),
        };

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains(scenario == "negated"
                ? " NOT IN ("
                : " IN (",
            sql,
            StringComparison.Ordinal);

        Assert.Contains("JSON_TABLE(@", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT COUNT(*)", sql, StringComparison.Ordinal);
    }

    /// <summary>Nullable parameters retain EF's explicit null compensation.</summary>
    /// <param name="mariaDb">Whether the MariaDB profile is configured.</param>
    /// <param name="char36">Whether the compared property stores textual GUIDs.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Nullable_independent_guid_membership_retains_null_compensation(
        bool mariaDb,
        bool char36
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb: mariaDb);
        Guid?[] values = [s_first, null];
        var query = char36
            ? context.Items.Where(item => EF
                .Parameter(values)
                .Contains(item.OptionalCharId))
            : context.Items.Where(item => EF
                .Parameter(values)
                .Contains(item.OptionalBinaryId));

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("JSON_TABLE(@", sql, StringComparison.Ordinal);
    }

    /// <summary>Unencoded native scalar parameters keep the existing JSON_CONTAINS fast path.</summary>
    /// <param name="mariaDb">Whether the MariaDB profile is configured.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Independent_native_scalar_membership_keeps_json_contains(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb: mariaDb);
        int[] values = [1, 2];

        // Act
        var sql = context
            .Items
            .Where(item => EF
                .Parameter(values)
                .Contains(item.Id))
            .ToQueryString();

        // Assert
        Assert.Contains("JSON_CONTAINS(@", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_TABLE(", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT COUNT(*)", sql, StringComparison.Ordinal);
    }

    /// <summary>Entity-backed decoded collections keep the correlated scalar workaround.</summary>
    /// <param name="mariaDb">Whether the MariaDB profile is configured.</param>
    /// <param name="char36">Whether model collection elements use textual GUIDs.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Correlated_guid_document_membership_keeps_scalar_count(
        bool mariaDb,
        bool char36
    )
    {
        // Arrange
        using var context = CreateContext(
            char36
                ? MySqlGuidFormat.Char36
                : MySqlGuidFormat.Binary16,
            mariaDb: mariaDb);

        // Act
        var sql = context
            .Items
            .Where(item => item.GuidValues.Contains(s_first))
            .ToQueryString();

        // Assert
        Assert.Contains("SELECT COUNT(*)", sql, StringComparison.Ordinal);
        Assert.Contains("JSON_TABLE(`i`.`GuidValues`", sql, StringComparison.Ordinal);
    }

    /// <summary>Filtering an independent decoded collection retains its composed predicate boundary.</summary>
    /// <param name="mariaDb">Whether the MariaDB profile is configured.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Filtered_independent_string_membership_keeps_scalar_count(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb: mariaDb);
        string[] values = ["alpha", "x"];

        // Act
        var sql = context
            .Items
            .Where(item => EF
                .Parameter(values)
                .Where(value => value.Length > 1)
                .Contains(item.Name))
            .ToQueryString();

        // Assert
        Assert.Contains("SELECT COUNT(*)", sql, StringComparison.Ordinal);
        Assert.Contains("CHAR_LENGTH(", sql, StringComparison.Ordinal);
        Assert.Contains("FROM_BASE64(", sql, StringComparison.Ordinal);
    }
}
