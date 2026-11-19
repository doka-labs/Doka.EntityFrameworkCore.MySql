namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlCteTranslationTests
{
    /// <summary>An unlabeled computed result gets a valid private derived-table column without changing its ordinal.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recursive_guard_handles_an_unlabeled_computed_scalar_projection(
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
            .WithRecursiveCte(frontier => frontier.Select(value => value + 1),
                rows => rows.Select(value => value + 2),
                3)
            .ToQueryString();

        // Assert
        Assert.Contains("WITH RECURSIVE", sql, StringComparison.Ordinal);
        Assert.Contains("__doka_recursive_col_0", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("``", sql, StringComparison.Ordinal);
        Assert.Contains("+ 2", sql, StringComparison.Ordinal);
    }

    /// <summary>The Count terminal retains a valid derived payload even though EF leaves its SQL alias empty.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recursive_guard_handles_the_actual_count_terminal_before_database_io(
        bool mariaDb
    )
    {
        // Arrange
        var capture = new CteCommandCapture();
        var opening = new CteConnectionOpeningCounter(suppressOpen: true);
        var version = mariaDb
            ? MySqlServerVersion.MariaDb(new Version(12, 3, 3))
            : MySqlServerVersion.MySql(new Version(8, 4, 0));

        using var context = CreateMutationContext(version, capture, opening);
        var query = context
            .Rows
            .Select(row => row.Id)
            .WithRecursiveCte(frontier => frontier.Select(value => value + 1), rows => rows, 3);

        // Act
        var exception = Record.Exception(() => ExecuteRecursiveCount(query));

        // Assert
        Assert.IsType<CteCommandCapturedException>(exception);
        Assert.Contains("COUNT(", capture.Sql!, StringComparison.Ordinal);
        Assert.Contains("__doka_recursive_col_0", capture.Sql!, StringComparison.Ordinal);
        Assert.DoesNotContain("``", capture.Sql!, StringComparison.Ordinal);
        Assert.True(opening.SuppressedOpenings > 0);
        Assert.Equal(opening.Attempts, opening.SuppressedOpenings);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Two rejoined entities can share visible Id/Name labels while the derived payload uses unique ordinal names.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recursive_guard_preserves_two_entity_projections_with_repeated_visible_column_labels(
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
                rows => rows.Join(context.Rows,
                    row => row.Id,
                    other => other.Id,
                    (first, second) => new
                    {
                        First = first,
                        Second = second,
                    }),
                3)
            .ToQueryString();

        var aliases = Regex
            .Matches(sql,
                @" AS `(?<name>__doka_recursive_col_\d+(?:_\d+)?)`")
            .Select(match => match.Groups["name"].Value)
            .ToArray();

        // Assert
        Assert.Contains("WITH RECURSIVE", sql, StringComparison.Ordinal);
        Assert.True(aliases.Length > 2, sql);
        Assert.Equal(aliases.Length,
            aliases
                .Distinct(StringComparer.Ordinal)
                .Count());

        Assert.DoesNotContain("``", sql, StringComparison.Ordinal);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Captures the synchronous scalar terminal at CommandInitialized while its native connection opening is suppressed.</summary>
    private static int ExecuteRecursiveCount(
        IQueryable<int> source
    )
    {
        // WHY: This probe captures the actual terminal; its fixture suppresses EF's earlier native opening.
        // ReSharper disable once MethodHasAsyncOverload
        return source.Count();
    }
}
