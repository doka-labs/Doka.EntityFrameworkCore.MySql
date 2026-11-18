namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Verifies JSON membership dispatch from provider-side element types and exact member paths.</summary>
public sealed class MySqlJsonMembershipConversionTests
{
    /// <summary>Supplies both engine profiles, membership directions and boolean model values.</summary>
    public static IEnumerable<TheoryDataRow<bool, bool, bool>> BooleanCases
    {
        get
        {
            foreach (var mariaDb in new[] { false, true })
            {
                foreach (var negated in new[] { false, true })
                {
                    foreach (var needle in new[] { false, true })
                    {
                        yield return new TheoryDataRow<bool, bool, bool>(mariaDb, negated, needle);
                    }
                }
            }
        }
    }

    /// <summary>A model string converted to an integer remains a numeric JSON candidate on both engines.</summary>
    /// <param name="mariaDb">Whether to use MariaDB 11.8.9 rather than MySQL 8.4.</param>
    /// <param name="negated">Whether the membership predicate is negated.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void String_to_int_elements_use_numeric_json_candidates(
        bool mariaDb,
        bool negated
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = negated
            ? context
                .Set<ConvertedCollectionRow>()
                .Where(row => !row.IntegerTexts.Contains("0012"))
            : context
                .Set<ConvertedCollectionRow>()
                .Where(row => row.IntegerTexts.Contains("0012"));

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains(negated
                ? "NOT JSON_CONTAINS("
                : "JSON_CONTAINS(",
            sql,
            StringComparison.Ordinal);

        Assert.Contains("JSON_ARRAY(12)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("'0012'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_TYPE(", sql, StringComparison.Ordinal);
    }

    /// <summary>Boolean elements converted to integers use zero/one JSON numbers, not JSON boolean tokens.</summary>
    /// <param name="mariaDb">Whether to use MariaDB 11.8.9 rather than MySQL 8.4.</param>
    /// <param name="negated">Whether the membership predicate is negated.</param>
    /// <param name="needle">The boolean model-side value.</param>
    [Theory]
    [MemberData(nameof(BooleanCases))]
    public void Bool_to_int_elements_use_numeric_json_candidates(
        bool mariaDb,
        bool negated,
        bool needle
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = (negated, needle) switch
        {
            (false, false) => context
                .Set<ConvertedCollectionRow>()
                .Where(row => row.IntegerFlags.Contains(false)),
            (false, true) => context
                .Set<ConvertedCollectionRow>()
                .Where(row => row.IntegerFlags.Contains(true)),
            (true, false) => context
                .Set<ConvertedCollectionRow>()
                .Where(row => !row.IntegerFlags.Contains(false)),
            (true, true) => context
                .Set<ConvertedCollectionRow>()
                .Where(row => !row.IntegerFlags.Contains(true)),
        };

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains(negated
                ? "NOT JSON_CONTAINS("
                : "JSON_CONTAINS(",
            sql,
            StringComparison.Ordinal);

        Assert.Contains(needle
                ? "JSON_ARRAY(1)"
                : "JSON_ARRAY(0)",
            sql,
            StringComparison.Ordinal);

        Assert.DoesNotContain("THEN 'true' ELSE 'false'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_TYPE(", sql, StringComparison.Ordinal);
    }

    /// <summary>Boolean elements with explicit string mappings keep those strings and use the correct engine branch.</summary>
    /// <param name="mariaDb">Whether to use MariaDB 11.8.9 rather than MySQL 8.4.</param>
    /// <param name="negated">Whether the membership predicate is negated.</param>
    /// <param name="needle">The boolean model-side value.</param>
    [Theory]
    [MemberData(nameof(BooleanCases))]
    public void Bool_to_string_elements_use_explicit_mapped_strings(
        bool mariaDb,
        bool negated,
        bool needle
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = (negated, needle) switch
        {
            (false, false) => context
                .Set<ConvertedCollectionRow>()
                .Where(row => row.TextFlags.Contains(false)),
            (false, true) => context
                .Set<ConvertedCollectionRow>()
                .Where(row => row.TextFlags.Contains(true)),
            (true, false) => context
                .Set<ConvertedCollectionRow>()
                .Where(row => !row.TextFlags.Contains(false)),
            (true, true) => context
                .Set<ConvertedCollectionRow>()
                .Where(row => !row.TextFlags.Contains(true)),
        };

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains(needle
                ? "'enabled'"
                : "'disabled'",
            sql,
            StringComparison.Ordinal);

        Assert.DoesNotContain("THEN 'true' ELSE 'false'", sql, StringComparison.Ordinal);
        if (mariaDb)
        {
            Assert.DoesNotContain("JSON_CONTAINS(", sql, StringComparison.Ordinal);
            Assert.Contains("JSON_TABLE(", sql, StringComparison.Ordinal);
            Assert.Contains("JSON_TYPE(", sql, StringComparison.Ordinal);
            Assert.Contains("AS BINARY", sql, StringComparison.Ordinal);
            Assert.Contains(negated
                    ? ") IS NULL END)"
                    : ") IS NOT NULL END)",
                sql,
                StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains(negated
                    ? "NOT JSON_CONTAINS("
                    : "JSON_CONTAINS(",
                sql,
                StringComparison.Ordinal);

            Assert.Contains("JSON_ARRAY(", sql, StringComparison.Ordinal);
            Assert.Contains("USING utf8mb4)", sql, StringComparison.Ordinal);
        }
    }

    /// <summary>Unrepresentable model-side strings retain converter failure before a database command can execute.</summary>
    /// <param name="mariaDb">Whether to use MariaDB 11.8.9 rather than MySQL 8.4.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void String_to_int_membership_rejects_an_invalid_model_value(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context
            .Set<ConvertedCollectionRow>()
            .Where(row => row.IntegerTexts.Contains("not-an-integer"));

        // Act
        var exception = Record.Exception(query.ToQueryString);

        // Assert
        Assert.IsType<FormatException>(exception);
    }

    /// <summary>A JSON object member called value is not membership of a scalar in the entire object array.</summary>
    /// <param name="mariaDb">Whether to use MariaDB 11.8.9 rather than MySQL 8.4.</param>
    /// <param name="negated">Whether the projected membership predicate is negated.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Owned_json_member_named_value_preserves_its_member_path(
        bool mariaDb,
        bool negated
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = negated
            ? context
                .Set<OwnedCollectionRow>()
                .Where(row => !row
                    .Items
                    .Select(item => item.Value)
                    .Contains("needle"))
            : context
                .Set<OwnedCollectionRow>()
                .Where(row => row
                    .Items
                    .Select(item => item.Value)
                    .Contains("needle"));

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("JSON_TABLE(", sql, StringComparison.Ordinal);
        Assert.Contains("PATH '$.value'", sql, StringComparison.Ordinal);
        Assert.Contains("'needle'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_CONTAINS(", sql, StringComparison.Ordinal);
    }

    private static MembershipContext CreateContext(
        bool mariaDb
    )
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<MembershipContext>();
        builder.UseMySql("Server=localhost;Database=unused;User ID=root;Password=unused;",
            mariaDb
                ? MySqlServerVersion.MariaDb(new Version(11, 8, 9))
                : MySqlServerVersion.MySql(new Version(8, 4, 0)));

        return new MembershipContext(builder.Options);
    }

    private sealed class MembershipContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public MembershipContext(
            DbContextOptions<MembershipContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<ConvertedCollectionRow>(entity =>
            {
                entity.HasKey(row => row.Id);
                entity
                    .PrimitiveCollection(row => row.IntegerTexts)
                    .ElementType()
                    .HasConversion<int>();

                entity
                    .PrimitiveCollection(row => row.IntegerFlags)
                    .ElementType()
                    .HasConversion<int>();

                entity
                    .PrimitiveCollection(row => row.TextFlags)
                    .ElementType()
                    .HasConversion(new BoolToStringConverter("disabled", "enabled"));
            });

            modelBuilder.Entity<OwnedCollectionRow>(entity =>
            {
                entity.HasKey(row => row.Id);
                entity.OwnsMany(row => row.Items,
                    owned =>
                    {
                        owned.ToJson();
                        owned
                            .Property(item => item.Value)
                            .HasJsonPropertyName("value");

                        owned
                            .Property(item => item.Other)
                            .HasJsonPropertyName("other");
                    });
            });
        }
    }

    private sealed class ConvertedCollectionRow
    {
        public int Id { get; set; }
        public string[] IntegerTexts { get; set; } = [];
        public bool[] IntegerFlags { get; set; } = [];
        public bool[] TextFlags { get; set; } = [];
    }

    private sealed class OwnedCollectionRow
    {
        public int Id { get; set; }
        public List<OwnedCollectionItem> Items { get; set; } = [];
    }

    private sealed class OwnedCollectionItem
    {
        public string Value { get; set; } = string.Empty;
        public int Other { get; set; }
    }
}
