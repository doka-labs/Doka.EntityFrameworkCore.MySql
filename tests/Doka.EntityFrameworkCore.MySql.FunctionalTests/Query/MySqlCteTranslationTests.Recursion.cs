namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlCteTranslationTests
{
    /// <summary>A scalar frontier advances in native UNION ALL recursion without a client loop.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recursive_scalar_projection_has_a_finite_frontier_and_hidden_depth(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var sql = context
            .Rows
            .Where(row => row.Id == 1)
            .Select(row => row.Id)
            .WithRecursiveCte(frontier => frontier.Select(value => value + 1), rows => rows.OrderBy(value => value), 3)
            .ToQueryString();

        // Assert
        Assert.Contains("WITH RECURSIVE", sql, StringComparison.Ordinal);
        Assert.Contains("UNION ALL", sql, StringComparison.Ordinal);
        Assert.Contains("`__doka_depth`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("WHERE 1 = 0", sql, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(sql, "FROM `CteRows`"));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Recursive joins preserve the mapped frontier and the separately filtered physical child source.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recursive_entity_join_retains_both_source_filter_scopes(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var sql = context
            .Rows
            .Where(row => row.Id == 1)
            .WithRecursiveCte(
                frontier => frontier.Join(context.Rows,
                    parent => parent.Id,
                    child => child.ParentId,
                    (_, child) => child),
                rows => rows.OrderBy(row => row.Id),
                4)
            .ToQueryString();

        // Assert
        Assert.Contains("WITH RECURSIVE", sql, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(sql, "FROM `CteRows`") + CountOccurrences(sql, "JOIN `CteRows`"));
        Assert.Contains("`ParentId`", sql, StringComparison.Ordinal);
        Assert.Contains("`TenantId`", sql, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN", sql, StringComparison.Ordinal);
    }

    /// <summary>Simple correlated recursive sources become direct joins even when the ordinary engine supports LATERAL.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recursive_correlated_select_many_uses_a_direct_predicate_join(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var source = context.Rows;

        // Act
        var sql = source
            .Where(row => row.Id == 1)
            .Select(row => row.Id)
            .WithRecursiveCte(
                frontier => frontier.SelectMany(id => source
                    .Where(row => row.Id == id + 1)
                    .Select(row => row.Id)),
                rows => rows,
                2)
            .ToQueryString();

        // Assert
        Assert.Contains("WITH RECURSIVE", sql, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN", sql, StringComparison.Ordinal);
        Assert.Contains("+ 1", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("LATERAL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("APPLY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`TenantId` =", sql, StringComparison.Ordinal);
    }

    /// <summary>The recursion API retains a zero semantic depth rather than substituting one or a server default.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Zero_depth_is_preserved_in_the_recursive_predicate(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var sql = context
            .Rows
            .Select(row => row.Id)
            .WithRecursiveCte(frontier => frontier.Select(value => value + 1), rows => rows, 0)
            .ToQueryString();

        // Assert
        Assert.Contains("WITH RECURSIVE", sql, StringComparison.Ordinal);
        Assert.Matches(@"`__doka_depth`\s*<\s*0", sql);
        Assert.DoesNotContain("SET SESSION", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Nullable scalar projection remains genuinely nullable through EF's set-operation alignment.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Nullable_recursive_projection_retains_the_nullable_value_shape(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var sql = context
            .Rows
            .Select(row => (int?)row.Id)
            .WithRecursiveCte(frontier => frontier.Select(value => value + 1), rows => rows, 3)
            .ToQueryString();

        // Assert
        Assert.Contains("UNION ALL", sql, StringComparison.Ordinal);
        Assert.Contains("`__doka_depth`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("COALESCE", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A nonnull anchor cannot erase a NULL test which observes values produced by later iterations.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Nullable_recursive_transition_retains_its_frontier_null_test(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var sql = context
            .Rows
            .Where(row => row.Id == 1)
            .Select(row => (int?)row.Id)
            .WithRecursiveCte(
                frontier => frontier.Select(value => value == null
                    ? (int?)1
                    : null),
                rows => rows,
                2)
            .ToQueryString();

        // Assert
        Assert.Contains("IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("CASE", sql, StringComparison.Ordinal);
        Assert.Contains("UNION ALL", sql, StringComparison.Ordinal);
    }

    /// <summary>An unused frontier cannot silently turn a repeated physical query into recursive traversal.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recursive_step_without_its_frontier_is_rejected(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context
            .Rows
            .Select(row => row.Id)
            .WithRecursiveCte(_ => context.Rows.Select(row => row.Id), rows => rows, 3);

        // Act
        var exception = Record.Exception(query.ToQueryString);

        // Assert
        Assert.Contains("exactly one frontier",
            Assert.IsType<InvalidOperationException>(exception)
                .Message,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Repeated self-reference fails instead of generating unsupported native mutual or multiple recursion.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Multiple_recursive_frontier_occurrences_are_rejected(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context
            .Rows
            .Select(row => row.Id)
            .WithRecursiveCte(
                frontier => frontier.Join(frontier, left => left, right => right, (left, right) => left + right),
                rows => rows,
                3);

        // Act
        var exception = Record.Exception(query.ToQueryString);

        // Assert
        Assert.Contains("exactly one frontier",
            Assert.IsType<InvalidOperationException>(exception)
                .Message,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Paging in the recursive arm cannot be erased by ordinary UNION normalization.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recursive_step_paging_is_rejected_before_normalization_can_remove_it(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context
            .Rows
            .Select(row => row.Id)
            .WithRecursiveCte(frontier => frontier.Take(1), rows => rows, 3);

        // Act
        var exception = Record.Exception(query.ToQueryString);

        // Assert
        Assert.Contains("paging",
            Assert.IsType<InvalidOperationException>(exception)
                .Message,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Ordinary CTE relations do not acquire the locking API's direct physical-table contract.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Locking_an_ordinary_cte_is_rejected_before_database_io(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context
            .Rows
            .WithCte(rows => rows)
            .ForUpdate();

        // Act
        var exception = Record.Exception(query.ToQueryString);

        // Assert
        Assert.Contains("locking",
            Assert.IsType<InvalidOperationException>(exception)
                .Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Recursive CTE relations are not directly lockable physical InnoDB table sources.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Locking_a_recursive_cte_is_rejected_before_database_io(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context
            .Rows
            .WithRecursiveCte(frontier => frontier, rows => rows, 3)
            .ForUpdate();

        // Act
        var exception = Record.Exception(query.ToQueryString);

        // Assert
        Assert.Contains("locking",
            Assert.IsType<InvalidOperationException>(exception)
                .Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Empty(context.ChangeTracker.Entries());
    }
}
