namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies failure composition for provider-managed migration cleanup.
/// </summary>
public sealed class MySqlScopedMigrationCommandTests
{
    [Fact]
    public void Primary_failure_is_rethrown_without_wrapping()
    {
        var primary = new InvalidOperationException("primary");

        var actual = Assert.Throws<InvalidOperationException>(() =>
            MySqlScopedMigrationCommand.ThrowFailures(primary, cleanupException: null));

        Assert.Same(primary, actual);
    }

    [Fact]
    public void Cleanup_failure_is_terminal_and_preserves_its_cause()
    {
        var cleanup = new InvalidOperationException("cleanup");

        var actual = Assert.Throws<MySqlMigrationSessionCleanupException>(() =>
            MySqlScopedMigrationCommand.ThrowFailures(primaryException: null, cleanupException: cleanup));

        Assert.Same(cleanup, actual.InnerException);
        Assert.Contains("automatic retry is disabled", actual.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Primary_and_cleanup_failures_remain_observable()
    {
        var primary = new InvalidOperationException("primary");
        var cleanup = new InvalidOperationException("cleanup");

        var actual = Assert.Throws<MySqlMigrationSessionCleanupException>(() =>
            MySqlScopedMigrationCommand.ThrowFailures(primary, cleanup));

        var failures = Assert.IsType<AggregateException>(actual.InnerException);

        Assert.Collection(
            failures.InnerExceptions,
            exception => Assert.Same(primary, exception),
            exception => Assert.Same(cleanup, exception));
    }
}
