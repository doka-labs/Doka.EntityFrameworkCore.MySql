namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlCteTranslationTests
{
    /// <summary>All aliases contribute before an ordinary fifty-column source is narrowed to its common schema.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Fifty_column_cte_keeps_the_union_of_all_reference_demands(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var sql = context
            .Set<CteWideRow>()
            .AsNoTracking()
            .WithCte(rows =>
                rows.Join(rows,
                    first => first.Id,
                    second => second.ParentId ?? 0,
                    (first, second) => new
                    {
                        First = EF.Property<int>(first, "Payload01"),
                        Second = EF.Property<int>(second, "Payload48"),
                    }))
            .ToQueryString();

        // Assert
        Assert.Equal(["Id", "ParentId", "Payload01", "Payload48"], ReadCteHeader(sql, "__doka_cte_"));
        Assert.Equal(1, CountOccurrences(sql, "FROM `CteWideRows`"));
        Assert.DoesNotContain("`Payload02`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`Payload47`", sql, StringComparison.Ordinal);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Recursive join and predicate fields survive even when the final payload does not return them.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Fifty_column_recursive_cte_retains_internal_frontier_dependencies_only(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var sql = context
            .Set<CteWideRow>()
            .AsNoTracking()
            .Where(row => row.Id == 1)
            .WithRecursiveCte(
                frontier => frontier
                    .Where(parent => EF.Property<int>(parent, "Payload47") < 10)
                    .Join(context.Set<CteWideRow>(),
                        parent => parent.Id,
                        child => child.ParentId,
                        (_, child) => child),
                rows => rows.Select(row => EF.Property<int>(row, "Payload48")),
                3)
            .ToQueryString();

        // Assert
        Assert.Equal(["Id", "Payload47", "Payload48", "__doka_depth"],
            ReadCteHeader(sql, "__doka_recursive_"));

        Assert.Contains("`ParentId`", sql, StringComparison.Ordinal);
        Assert.Contains("`Payload47` < 10", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`Payload01`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`Payload46`", sql, StringComparison.Ordinal);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Projection pruning cannot change the equivalence relation of a fifty-column DISTINCT source.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Fifty_column_distinct_cte_preserves_all_duplicate_discriminating_fields(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var sql = context
            .Set<CteWideRow>()
            .AsNoTracking()
            .Distinct()
            .WithCte(rows => rows.Select(row => EF.Property<int>(row, "Payload48")))
            .ToQueryString();

        // Assert
        var header = ReadCteHeader(sql, "__doka_cte_");
        Assert.Equal(50, header.Length);
        Assert.Contains("Payload01", header);
        Assert.Contains("Payload48", header);
        Assert.Contains("SELECT DISTINCT", sql, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(sql, "FROM `CteWideRows`"));
    }

    /// <summary>The recursive row-only consumer retains one aligned visible ordinal plus hidden depth.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recursive_cte_with_constant_outer_payload_keeps_aligned_arm_arity(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var sql = context
            .Set<CteWideRow>()
            .AsNoTracking()
            .WithRecursiveCte(frontier => frontier, rows => rows.Select(_ => 1), 0)
            .ToQueryString();

        // Assert
        var header = ReadCteHeader(sql, "__doka_recursive_");
        Assert.Equal(2, header.Length);
        Assert.Equal("__doka_depth", header[1]);
        Assert.Contains("UNION ALL", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`Payload48`", sql, StringComparison.Ordinal);
    }

    /// <summary>A genuinely empty DTO source still declares the visible sentinel required by both SQL arms.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recursive_cte_with_an_empty_dto_keeps_its_declared_visible_sentinel(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var sql = context
            .Set<CteWideRow>()
            .AsNoTracking()
            .Select(_ => new { })
            .WithRecursiveCte(frontier => frontier, rows => rows, 0)
            .ToQueryString();

        // Assert
        Assert.Equal(["__empty", "__doka_depth"], ReadCteHeader(sql, "__doka_recursive_"));
        Assert.Equal(2, Regex.Count(sql, @"SELECT 1 AS `__empty`,"));
        Assert.Contains("UNION ALL", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`Payload48`", sql, StringComparison.Ordinal);
    }

    /// <summary>Reads the actual declared common schema rather than counting unrelated outer SQL references.</summary>
    private static string[] ReadCteHeader(
        string sql,
        string prefix
    )
    {
        var match = Regex.Match(sql,
            "`" + Regex.Escape(prefix) + @"\d+(?:_\d+)?` \((?<columns>[^)]+)\) AS \(");

        Assert.True(match.Success, sql);

        return match
            .Groups["columns"]
            .Value
            .Split(", ")
            .Select(name => name.Trim('`'))
            .ToArray();
    }
}
