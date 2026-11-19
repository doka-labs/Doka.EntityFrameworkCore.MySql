namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlWindowFunctionTests
{
    /// <summary>A row-dependent default retains the value column's compatible relational representation.</summary>
    /// <param name="mariaDb">Whether to use the MariaDB dialect.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Lead_accepts_a_same_mapping_column_as_its_missing_neighbor_default(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context.Rows.Select(row => EF
            .Functions
            .Window()
            .OrderBy(row.Id)
            .Lead(row.NullableValue, 1, row.Sequence));

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("LEAD(", sql, StringComparison.Ordinal);
        Assert.Contains("`Sequence`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("COALESCE(LEAD", sql, StringComparison.Ordinal);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Equal CLR GUID types do not authorize a binary default for a textual window value.</summary>
    /// <param name="mariaDb">Whether to use the MariaDB dialect.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Lag_rejects_a_column_default_with_an_incompatible_guid_store_representation(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context.Rows.Select(row => EF
            .Functions
            .Window()
            .OrderBy(row.Id)
            .Lag<Guid>(row.GuidValue, 1, row.BinaryGuidValue));

        // Act
        var error = Record.Exception(() => query.ToQueryString());

        // Assert
        Assert.Contains("compatible mapped representation",
            Assert.IsType<InvalidOperationException>(error)
                .Message,
            StringComparison.Ordinal);

        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>A later window OrderBy replaces the former key while ThenByDescending retains its direction.</summary>
    /// <param name="mariaDb">Whether to use the MariaDB dialect.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Replaced_window_ordering_retains_only_the_last_primary_key_and_descending_tie_breaker(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context.Rows.Select(row => EF
            .Functions
            .Window()
            .OrderBy(row.Name)
            .OrderByDescending(row.Sequence)
            .ThenByDescending(row.Id)
            .Rank());

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("`Sequence` DESC,", sql, StringComparison.Ordinal);
        Assert.Contains("`Id` DESC)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`Name`", sql, StringComparison.Ordinal);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Numeric offset RANGE admits one ordering expression, not a lexicographic tuple.</summary>
    /// <param name="mariaDb">Whether to use the MariaDB dialect.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Numeric_range_rejects_multiple_ordering_keys_before_io(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context.Rows.Select(row => EF
            .Functions
            .Window()
            .OrderBy(row.Amount)
            .ThenBy(row.Id)
            .RangeBetween(MySqlWindowFrameBound.CurrentRow, MySqlWindowFrameBound.Following(1))
            .Sum(row.Amount));

        // Act
        var error = Record.Exception(() => query.ToQueryString());

        // Assert
        Assert.Contains("exactly one numeric",
            Assert.IsType<InvalidOperationException>(error)
                .Message,
            StringComparison.Ordinal);

        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>The expression-only builder cannot be materialized in place of a scalar calculation.</summary>
    /// <param name="mariaDb">Whether to use the MariaDB dialect.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dangling_window_definition_is_rejected_before_io(
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
        Assert.Equal("A window descriptor cannot escape its expression-only terminal calculation.",
            Assert.IsType<InvalidOperationException>(error)
                .Message);

        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>A scalar subquery completes its own window before an outer calculation consumes its value.</summary>
    /// <param name="mariaDb">Whether to use the MariaDB dialect.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Window_in_a_separate_scalar_subquery_is_not_rejected_as_same_level_nesting(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context.Rows.Select(row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .Sum(
                context
                    .Rows
                    .Where(inner => inner.Active)
                    .OrderBy(inner => inner.Id)
                    .Select(inner => EF
                        .Functions
                        .Window()
                        .OrderBy(inner.Id)
                        .RowNumber())
                    .First()));

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("SUM((", sql, StringComparison.Ordinal);
        Assert.Contains("ROW_NUMBER() OVER", sql, StringComparison.Ordinal);
        Assert.Contains("LIMIT 1", sql, StringComparison.Ordinal);
        Assert.Contains("PARTITION BY", sql, StringComparison.Ordinal);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }
}
