namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlCteTranslationTests
{
    /// <summary>The finite cast capacity follows actual mapped characters and selected iterations, preserving text semantics.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recursive_text_capacity_is_proved_before_frontier_and_outer_parameter_mapping(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var expected = new string('x', 103);

        // Act
        var sql = context
            .Rows
            .Select(row => EF.Functions.Collate(row.Name, "utf8mb4_unicode_ci"))
            .WithRecursiveCte(frontier => frontier.Select(value => value + "x"),
                rows => rows.Where(value => value == expected),
                3)
            .ToQueryString();

        // Assert
        Assert.Contains("AS CHAR(103) CHARACTER SET utf8mb4", sql, StringComparison.Ordinal);
        Assert.Contains("COLLATE utf8mb4_unicode_ci", sql, StringComparison.Ordinal);
        Assert.Matches(
            @"CAST\([^\r\n]+ COLLATE utf8mb4_unicode_ci AS CHAR\(103\) CHARACTER SET utf8mb4\) COLLATE utf8mb4_unicode_ci",
            sql);

        Assert.Contains(expected, sql, StringComparison.Ordinal);
        Assert.DoesNotContain("CHAR(255)", sql, StringComparison.Ordinal);
    }

    /// <summary>Literal anchors use Unicode scalar characters rather than arbitrary capacity or UTF-16 code-unit counts.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recursive_literal_text_capacity_is_derived_from_its_exact_bound(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var sql = context
            .Rows
            .Select(_ => EF.Functions.Collate("", "utf8mb4_unicode_ci"))
            .WithRecursiveCte(frontier => frontier.Select(value => value + "ab"), rows => rows, 4)
            .ToQueryString();

        // Assert
        Assert.Contains("AS CHAR(8) CHARACTER SET utf8mb4", sql, StringComparison.Ordinal);
        Assert.Contains("COLLATE utf8mb4_unicode_ci", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("CHAR(255)", sql, StringComparison.Ordinal);
    }

    /// <summary>A nominal CLR longtext fallback is not evidence that an empty native literal anchor can grow.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unknown_native_text_capacity_is_rejected_instead_of_truncating(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context
            .Rows
            .Select(_ => "")
            .WithRecursiveCte(frontier => frontier.Select(value => value + "x"), rows => rows, 3);

        // Act
        var exception = Record.Exception(query.ToQueryString);

        // Assert
        Assert.Contains("anchor capacity",
            Assert.IsType<InvalidOperationException>(exception)
                .Message,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Unknown concatenation input length cannot be replaced by an invented recursive cast width.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unbounded_recursive_text_parameter_growth_is_rejected(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var suffix = "x";
        var query = context
            .Rows
            .Select(_ => EF.Functions.Collate("", "utf8mb4_unicode_ci"))
            .WithRecursiveCte(frontier => frontier.Select(value => value + suffix), rows => rows, 3);

        // Act
        var exception = Record.Exception(query.ToQueryString);

        // Assert
        Assert.Contains("anchor capacity",
            Assert.IsType<InvalidOperationException>(exception)
                .Message,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>CTE-N07 rejects unbounded physical LONGTEXT growth; its declared capacity does not prove a safe packet bound.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Physical_longtext_growth_requires_a_proved_bound_instead_of_assuming_unlimited_capacity(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context
            .Rows
            .Select(row => row.UnboundedName)
            .WithRecursiveCte(frontier => frontier.Select(value => value + "x"), rows => rows, 3);

        // Act
        var exception = Record.Exception(query.ToQueryString);

        // Assert
        Assert.Contains("anchor capacity",
            Assert.IsType<InvalidOperationException>(exception)
                .Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>The original physical charset mismatch reaches native validation before any provider-added widening conversion.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Widening_preserves_an_original_incompatible_collation_inside_the_cast(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var sql = context
            .Rows
            .Select(row => EF.Functions.Collate(row.Latin1Name, "utf8mb4_unicode_ci"))
            .WithRecursiveCte(frontier => frontier.Select(value => value + "x"), rows => rows, 3)
            .ToQueryString();

        // Assert
        // WHY: Native proof requires error1253 for this inner COLLATE. Applying only an outer
        // COLLATE after the conversion would silently turn the same invalid expression into text.
        Assert.Matches(
            @"CAST\([^\r\n]+`Latin1Name` COLLATE utf8mb4_unicode_ci AS CHAR\(103\) CHARACTER SET utf8mb4\) COLLATE utf8mb4_unicode_ci",
            sql);

        Assert.DoesNotContain("CONVERT(", sql, StringComparison.Ordinal);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>CTE-P21 bounded casts do not require rejecting unchanged physical LONGTEXT values.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Physical_longtext_identity_recursion_retains_its_existing_native_mapping(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var sql = context
            .Rows
            .Select(row => row.UnboundedName)
            .WithRecursiveCte(frontier => frontier, rows => rows, 3)
            .ToQueryString();

        // Assert
        Assert.Contains("WITH RECURSIVE", sql, StringComparison.Ordinal);
        Assert.Contains("`UnboundedName`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("AS CHAR(", sql, StringComparison.Ordinal);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>A same-facet physical value copied by the recursive step is not an unbounded string transformation.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Physical_longtext_step_can_copy_a_matching_physical_column_without_a_cast(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var sql = context
            .Rows
            .Select(row => row.UnboundedName)
            .WithRecursiveCte(
                frontier => frontier.Join(context.Rows,
                    value => value,
                    row => row.UnboundedName,
                    (_, row) => row.UnboundedName),
                rows => rows,
                3)
            .ToQueryString();

        // Assert
        Assert.Contains("WITH RECURSIVE", sql, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("AS CHAR(", sql, StringComparison.Ordinal);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
