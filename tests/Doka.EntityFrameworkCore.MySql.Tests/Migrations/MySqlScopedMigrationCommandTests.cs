namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies failure composition for provider-managed migration cleanup.
/// </summary>
public sealed class MySqlScopedMigrationCommandTests
{
    /// <summary>
    /// Verifies primary failure is rethrown without wrapping.
    /// </summary>
    [Fact]
    public void Primary_failure_is_rethrown_without_wrapping()
    {
        // Arrange
        var primary = new InvalidOperationException("primary");
        Action act = () =>
            MySqlScopedMigrationCommand.ThrowFailures(primary, cleanupException: null);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var actual = Assert.IsType<InvalidOperationException>(recordedException);
        Assert.Same(primary, actual);
    }

    /// <summary>
    /// Verifies cleanup failure is terminal and preserves its cause.
    /// </summary>
    [Fact]
    public void Cleanup_failure_is_terminal_and_preserves_its_cause()
    {
        // Arrange
        var cleanup = new InvalidOperationException("cleanup");
        Action act = () =>
            MySqlScopedMigrationCommand.ThrowFailures(primaryException: null, cleanupException: cleanup);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var actual = Assert.IsType<MySqlMigrationSessionCleanupException>(recordedException);
        Assert.Same(cleanup, actual.InnerException);
        Assert.Contains("automatic retry is disabled", actual.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies primary and cleanup failures remain observable.
    /// </summary>
    [Fact]
    public void Primary_and_cleanup_failures_remain_observable()
    {
        // Arrange
        var primary = new InvalidOperationException("primary");
        var cleanup = new InvalidOperationException("cleanup");
        Action act = () =>
            MySqlScopedMigrationCommand.ThrowFailures(primary, cleanup);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var actual = Assert.IsType<MySqlMigrationSessionCleanupException>(recordedException);
        var failures = Assert.IsType<AggregateException>(actual.InnerException);
        Assert.Collection(
            failures.InnerExceptions,
            exception => Assert.Same(primary, exception),
            exception => Assert.Same(cleanup, exception));
    }
}
