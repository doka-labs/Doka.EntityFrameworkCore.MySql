namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlIndexedSelectTests
{
    /// <summary>A bare definition cannot become a top-level client projection after a data command.</summary>
    [Theory]
    [MemberData(nameof(Dialects))]
    public void Window_descriptor_cannot_escape_as_a_projection_value(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context.Rows.Select(row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId));

        // Act
        var error = Record.Exception(() => query.ToQueryString());

        // Assert
        Assert.Contains("descriptor cannot escape",
            Assert.IsType<InvalidOperationException>(error)
                .Message,
            StringComparison.Ordinal);
    }

    /// <summary>A preceding valid scalar does not hide a later escaping descriptor in the same projection.</summary>
    [Theory]
    [MemberData(nameof(Dialects))]
    public void Valid_window_sibling_does_not_hide_an_escaping_descriptor(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context.Rows.Select(row => new
        {
            Number = EF
                .Functions
                .Window()
                .OrderBy(row.Id)
                .RowNumber(),
            Descriptor = EF
                .Functions
                .Window()
                .PartitionBy(row.AccountId),
        });

        // Act
        var error = Record.Exception(() => query.ToQueryString());

        // Assert
        Assert.Contains("descriptor cannot escape",
            Assert.IsType<InvalidOperationException>(error)
                .Message,
            StringComparison.Ordinal);
    }

    /// <summary>Independent scalar windows remain legal siblings while their descriptor receivers stay expression-only.</summary>
    [Theory]
    [MemberData(nameof(Dialects))]
    public void Multiple_valid_window_siblings_remain_translatable(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context.Rows.Select(row => new
        {
            Number = EF
                .Functions
                .Window()
                .OrderBy(row.Id)
                .RowNumber(),
            Rank = EF
                .Functions
                .Window()
                .OrderBy(row.Amount)
                .Rank(),
        });

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Equal(1, Occurrences(sql, "ROW_NUMBER()"));
        Assert.Equal(1, Occurrences(sql, "RANK()"));
    }

    /// <summary>An index may consume an earlier projected rank column through a separate relational level.</summary>
    [Theory]
    [MemberData(nameof(Dialects))]
    public void Indexed_ordering_can_consume_a_previously_projected_window_column(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context
            .Rows
            .Select(row => new
            {
                row.Id,
                Rank = EF
                    .Functions
                    .Window()
                    .OrderBy(row.Amount)
                    .Rank(),
            })
            .OrderBy(row => row.Rank)
            .ThenBy(row => row.Id)
            .Select((row, index) => new
            {
                row.Id,
                row.Rank,
                Index = index,
            });

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Equal(1, Occurrences(sql, "ROW_NUMBER()"));
        Assert.Equal(1, Occurrences(sql, "RANK()"));
        Assert.True(Occurrences(sql, "FROM (") >= 2, sql);
    }

    /// <summary>A window directly inside the index ordering would be an illegal nested OVER calculation.</summary>
    [Theory]
    [MemberData(nameof(Dialects))]
    public void Indexed_ordering_cannot_nest_an_unprojected_window(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context
            .Rows
            .OrderBy(row => EF
                .Functions
                .Window()
                .OrderBy(row.Amount)
                .Rank())
            .Select((row, index) => new
            {
                row.Id,
                Index = index,
            });

        // Act
        var error = Record.Exception(() => query.ToQueryString());

        // Assert
        Assert.Contains("projected before",
            Assert.IsType<InvalidOperationException>(error)
                .Message,
            StringComparison.Ordinal);
    }

    /// <summary>A prefix-count predicate cannot secretly nest another window in the same relational calculation.</summary>
    [Theory]
    [MemberData(nameof(Dialects))]
    public void Filtered_group_index_cannot_nest_a_direct_window_predicate(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context
            .Rows
            .GroupBy(row => row.AccountId)
            .Select(group => group
                .Where(row => EF
                        .Functions
                        .Window()
                        .OrderBy(row.Id)
                        .RowNumber()
                    > 1)
                .OrderBy(row => row.Id)
                .Select((row, index) => new
                {
                    row.Id,
                    Index = index,
                })
                .ToList());

        // Act
        var error = Record.Exception(() => query.ToQueryString());

        // Assert
        Assert.Contains("projected before",
            Assert.IsType<InvalidOperationException>(error)
                .Message,
            StringComparison.Ordinal);
    }

    /// <summary>A projected window column may legitimately filter a later group's prefix-count index.</summary>
    [Theory]
    [MemberData(nameof(Dialects))]
    public void Filtered_group_index_can_consume_a_previously_projected_window_column(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context
            .Rows
            .Select(row => new
            {
                row.Id,
                row.AccountId,
                Rank = EF
                    .Functions
                    .Window()
                    .OrderBy(row.Amount)
                    .Rank(),
            })
            .GroupBy(row => row.AccountId)
            .Select(group => group
                .Where(row => row.Rank > 1)
                .OrderBy(row => row.Id)
                .Select((row, index) => new
                {
                    row.Id,
                    Index = index,
                })
                .ToList());

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("RANK()", sql, StringComparison.Ordinal);
        Assert.Contains("SUM(CASE", sql, StringComparison.Ordinal);
        Assert.Contains("ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW", sql, StringComparison.Ordinal);
    }

    /// <summary>Navigation expansion cannot inline a projected rank into its subsequent WHERE predicate.</summary>
    [Theory]
    [MemberData(nameof(Dialects))]
    public void Projected_window_filter_retains_a_completed_relational_scope(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context
            .Rows
            .Select(row => new
            {
                row.Id,
                Rank = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.AccountId)
                    .OrderBy(row.Stamp)
                    .ThenBy(row.Id)
                    .RowNumber(),
            })
            .Where(row => row.Rank <= 2);

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Equal(1, Occurrences(sql, "ROW_NUMBER()"));
        Assert.Contains("FROM (", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("WHERE ROW_NUMBER", sql, StringComparison.Ordinal);
    }

    /// <summary>A new window after paging evaluates only the selected rows rather than changing the page's source.</summary>
    [Theory]
    [MemberData(nameof(Dialects))]
    public void Window_introduced_after_paging_retains_the_prior_selection(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context
            .Rows
            .OrderBy(row => row.Id)
            .Skip(1)
            .Take(3)
            .Select(row => new
            {
                row.Id,
                Rank = EF
                    .Functions
                    .Window()
                    .OrderBy(row.Amount)
                    .ThenBy(row.Id)
                    .Rank(),
            });

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("RANK()", sql, StringComparison.Ordinal);
        Assert.Contains("LIMIT", sql, StringComparison.Ordinal);
        Assert.Contains("OFFSET", sql, StringComparison.Ordinal);
        Assert.True(Occurrences(sql, "FROM (") >= 2, sql);
    }

    /// <summary>A direct window predicate is invalid; callers must explicitly project its result before filtering.</summary>
    [Theory]
    [MemberData(nameof(Dialects))]
    public void Direct_window_predicate_is_rejected_before_sql_execution(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context.Rows.Where(row => EF
                .Functions
                .Window()
                .OrderBy(row.Id)
                .RowNumber()
            <= 2);

        // Act
        var error = Record.Exception(() => query.ToQueryString());

        // Assert
        Assert.Contains("projected before",
            Assert.IsType<InvalidOperationException>(error)
                .Message,
            StringComparison.Ordinal);
    }

    /// <summary>Different member orderings retain different partition ordinals instead of sharing mutable translator state.</summary>
    [Theory]
    [MemberData(nameof(Dialects))]
    public void Grouped_indexed_members_with_different_orderings_have_separate_ordinals(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context
            .Rows
            .GroupBy(row => row.AccountId)
            .Select(group => new
            {
                group.Key,
                Forward = group
                    .OrderBy(row => row.Id)
                    .Select((row, index) => new
                    {
                        row.Id,
                        Index = index,
                    })
                    .ToList(),
                Reverse = group
                    .OrderByDescending(row => row.Id)
                    .Select((row, index) => new
                    {
                        row.Id,
                        Index = index,
                    })
                    .ToList(),
            });

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.True(Occurrences(sql, "ROW_NUMBER() OVER(PARTITION BY") >= 2, sql);
        Assert.Contains("DESC", sql, StringComparison.Ordinal);
        Assert.Contains("GROUP BY", sql, StringComparison.Ordinal);
    }

    /// <summary>Member paging occurs after its retained-row rank and the returned LINQ index starts at zero for that page.</summary>
    [Theory]
    [MemberData(nameof(Dialects))]
    public void Filtered_group_member_page_keeps_a_distinct_zero_based_index_scope(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context
            .Rows
            .GroupBy(row => row.AccountId)
            .Select(group => new
            {
                group.Key,
                Items = group
                    .Where(row => row.Amount > 10)
                    .OrderBy(row => row.Id)
                    .Skip(1)
                    .Take(2)
                    .Select((row, index) => new
                    {
                        row.Id,
                        Index = index,
                    })
                    .ToList(),
            });

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("SUM(CASE", sql, StringComparison.Ordinal);
        Assert.Contains("ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW", sql, StringComparison.Ordinal);
        Assert.Contains("GROUP BY", sql, StringComparison.Ordinal);
    }
}
