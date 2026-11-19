namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlInsertSelectTranslationTests
{
    /// <summary>Recursive INSERT has one data statement and an independent error arm, including an empty payload.</summary>
    /// <param name="mariaDb">Whether the configured engine uses MariaDB's native recursion limit.</param>
    /// <param name="emptyPayload">Whether the completed recursive source selects no inserted rows.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Recursive_insert_uses_the_nonquery_guard_without_reader_control_columns(
        bool mariaDb,
        bool emptyPayload
    )
    {
        // Arrange
        var observation = new InsertObservation();
        await using var context = CreateContext(mariaDb, observation);
        var source = context
            .Sources
            .Where(row => row.Id == 1)
            .Select(row => row.Value)
            .WithRecursiveCte(frontier => frontier.Select(value => value + 1), rows => rows, 3);

        if (emptyPayload)
        {
            source = source.Take(0);
        }

        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var actual = await Record.ExceptionAsync(() => source.ExecuteInsertFromQueryAsync(context.Targets,
            setters => setters
                .SetProperty(row => row.Name, _ => "recursive-insert")
                .SetProperty(row => row.Value, value => value),
            cancellationToken));

        // Assert
        Assert.Same(observation.OpenFailure, actual);
        var sql = Assert.Single(observation.Commands)
            .Sql;

        Assert.StartsWith("INSERT INTO `insert``Target` (`Data``Name`, `payload value`)",
            sql,
            StringComparison.Ordinal);

        Assert.Equal(0, sql.LastIndexOf("INSERT INTO", StringComparison.Ordinal));
        Assert.Contains("WITH RECURSIVE", sql, StringComparison.Ordinal);
        Assert.Contains("SELECT (SELECT NULL UNION ALL SELECT NULL)", sql, StringComparison.Ordinal);
        Assert.Contains(mariaDb
                ? "@@max_recursive_iterations"
                : "@@cte_max_recursion_depth",
            sql,
            StringComparison.Ordinal);

        Assert.DoesNotContain("__doka_recursive_row", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("__doka_recursive_limit", sql, StringComparison.Ordinal);
        Assert.Equal(1, observation.OpenAttempts);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
