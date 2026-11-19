namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Verifies actual indexed LINQ and the relational boundaries required by projected windows.</summary>
public sealed partial class MySqlIndexedSelectTests
{
    /// <summary>Reports both native dialects for independent translation controls.</summary>
    public static TheoryData<bool> Dialects => [false, true];

    /// <summary>Reports native wide values whose checked LINQ index is representable.</summary>
    public static TheoryData<long, int> ValidIndices => new()
    {
        { 0L, 0 },
        { 1L, 1 },
        { int.MaxValue, int.MaxValue },
    };

    /// <summary>An ordered completed page is selected before its zero-based index is calculated.</summary>
    [Theory]
    [MemberData(nameof(Dialects))]
    public void Global_index_is_calculated_after_the_completed_source_page(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context
            .Rows
            .OrderBy(row => row.Stamp)
            .ThenBy(row => row.Id)
            .Skip(2)
            .Take(3)
            .Select((row, index) => new
            {
                row.Id,
                Index = index,
            });

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("ROW_NUMBER()", sql, StringComparison.Ordinal);
        Assert.Contains("LIMIT", sql, StringComparison.Ordinal);
        Assert.Contains("OFFSET", sql, StringComparison.Ordinal);
        Assert.True(Occurrences(sql, "FROM (") >= 2, sql);
        Assert.Equal(1, Occurrences(sql, "ROW_NUMBER()"));
    }

    /// <summary>A projected ordinal is a column in a later predicate, never a repeated OVER expression in WHERE.</summary>
    [Theory]
    [MemberData(nameof(Dialects))]
    public void Global_index_filter_uses_an_outer_select_scope(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context
            .Rows
            .OrderBy(row => row.Id)
            .Select((row, index) => new
            {
                row.Id,
                Index = index,
            })
            .Where(row => row.Index < 2);

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Equal(1, Occurrences(sql, "ROW_NUMBER()"));
        Assert.Contains("FROM (", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("WHERE ROW_NUMBER", sql, StringComparison.Ordinal);
    }

    /// <summary>Indexed LINQ does not invent an ordering when the source is unordered.</summary>
    [Theory]
    [MemberData(nameof(Dialects))]
    public void Unordered_indexed_select_is_rejected(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context.Rows.Select((row, index) => new
        {
            row.Id,
            Index = index,
        });

        // Act
        var error = Record.Exception(() => query.ToQueryString());

        // Assert
        Assert.Contains("explicit source ordering",
            Assert.IsType<InvalidOperationException>(error)
                .Message,
            StringComparison.Ordinal);
    }

    /// <summary>AllowUnsupported changes server support policy without inventing native window syntax.</summary>
    [Theory]
    [MemberData(nameof(Dialects))]
    public void Indexed_select_requires_native_window_support_even_when_old_versions_are_allowed(
        bool mariaDb
    )
    {
        // Arrange
        var version = mariaDb
            ? MySqlServerVersion.MariaDb(new Version(10, 1, 99), MySqlServerVersionCompatibilityMode.AllowUnsupported)
            : MySqlServerVersion.MySql(new Version(8, 0, 1), MySqlServerVersionCompatibilityMode.AllowUnsupported);

        using var context = CreateContext(mariaDb, version: version);
        var query = context
            .Rows
            .OrderBy(row => row.Id)
            .Select((row, index) => new
            {
                row.Id,
                Index = index,
            });

        // Act
        var error = Record.Exception(() => query.ToQueryString());

        // Assert
        Assert.Contains("does not support window",
            Assert.IsType<InvalidOperationException>(error)
                .Message,
            StringComparison.Ordinal);
    }

    /// <summary>The real grouped indexed selector retains EF's group aggregate and collection shape.</summary>
    [Theory]
    [MemberData(nameof(Dialects))]
    public void Grouped_indexed_selector_retains_count_and_ordered_items(
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
                Account = group.Key,
                Count = group.Count(),
                Items = group
                    .OrderBy(row => row.Stamp)
                    .ThenBy(row => row.Id)
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
        Assert.Contains("ROW_NUMBER() OVER(PARTITION BY", sql, StringComparison.Ordinal);
        Assert.Contains("COUNT(*)", sql, StringComparison.Ordinal);
        Assert.Contains("GROUP BY", sql, StringComparison.Ordinal);
    }

    /// <summary>A filtered group member counts retained rows rather than borrowing the original full-group ordinal.</summary>
    [Theory]
    [MemberData(nameof(Dialects))]
    public void Group_member_filter_indexes_only_the_retained_rows(
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
                Count = group.Count(),
                Items = group
                    .Where(row => row.Amount > 10)
                    .OrderBy(row => row.Stamp)
                    .ThenBy(row => row.Id)
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
        Assert.Contains("COUNT(*)", sql, StringComparison.Ordinal);
    }

    /// <summary>Distinct or filtering after a member page cannot reuse an index from a different relational scope.</summary>
    [Theory]
    [MemberData(nameof(Dialects))]
    public void Group_member_filter_after_a_page_is_not_silently_reindexed_from_the_full_group(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context
            .Rows
            .GroupBy(row => row.AccountId)
            .Select(group => group
                .OrderBy(row => row.Id)
                .Skip(1)
                .Where(row => row.Amount > 10)
                .Select((row, index) => new
                {
                    row.Id,
                    Index = index,
                })
                .ToList());

        // Act
        var error = Record.Exception(() => query.ToQueryString());

        // Assert
        Assert.Contains("separate child ranking scope",
            Assert.IsType<InvalidOperationException>(error)
                .Message,
            StringComparison.Ordinal);
    }

    /// <summary>The actual query materializer reads the native wide value and applies its checked model conversion.</summary>
    [Theory]
    [MemberData(nameof(ValidIndices))]
    public void Materialized_native_index_uses_the_checked_int_mapping(
        long nativeIndex,
        int expected
    )
    {
        // Arrange
        var values = new IndexValues(nativeIndex);
        using var context = CreateContext(false, values);
        var query = context
            .Rows
            .OrderBy(row => row.Id)
            .Select((row, index) => index);

        // Act
        var actual = query.ToArray();

        // Assert
        Assert.Equal([expected], actual);
        Assert.Equal(1, values.Commands);
        Assert.Equal(1, values.Int32Reads);
        Assert.Equal(1, values.ReaderCloses);
    }

    /// <summary>An index outside LINQ's int range fails rather than wrapping or truncating a native bigint.</summary>
    [Fact]
    public void Materialized_native_index_overflow_is_visible()
    {
        // Arrange
        var values = new IndexValues((long)int.MaxValue + 1);
        using var context = CreateContext(false, values);
        var query = context
            .Rows
            .OrderBy(row => row.Id)
            .Select((row, index) => index);

        // Act
        var error = Record.Exception(() => query.ToArray());

        // Assert
        Assert.IsType<OverflowException>(error);
        Assert.Equal(1, values.Commands);
        Assert.Equal(1, values.Int32Reads);
        Assert.Equal(1, values.ReaderCloses);
    }

    /// <summary>A normal nonindexed projection keeps its previous direct SQL shape.</summary>
    [Theory]
    [MemberData(nameof(Dialects))]
    public void Ordinary_projection_does_not_add_an_index_or_window_boundary(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context
            .Rows
            .OrderBy(row => row.Id)
            .Select(row => row.Id);

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.DoesNotContain("ROW_NUMBER", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("FROM (", sql, StringComparison.Ordinal);
    }
}
