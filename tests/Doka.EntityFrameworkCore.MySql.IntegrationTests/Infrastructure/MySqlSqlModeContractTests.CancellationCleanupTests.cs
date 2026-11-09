namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Verifies protected probe cleanup with deterministic task and failure boundaries.</summary>
public sealed partial class MySqlSqlModeContractTests
{
    /// <summary>Follow-up SQL cannot run before the owned command has finished.</summary>
    [Fact]
    public async Task Probe_cleanup_waits_for_pending_work_before_follow_up()
    {
        // Arrange
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new List<string>();
        var followUpBeforeCompletion = false;

        // Act
        // WHY: With no asynchronous cancellation callback, teardown reaches the pending await before
        // control returns here. Completing the task then cannot hide prematurely executed follow-up work.
        var cleanup = DatabaseLockProbeCleanup.CompleteLockProbeAsync(
            cancellation: null,
            () =>
            {
                events.Add("release");

                return Task.CompletedTask;
            },
            pending.Task,
            primaryFailure: null,
            () =>
            {
                followUpBeforeCompletion = !pending.Task.IsCompleted;
                events.Add("follow-up");

                return Task.CompletedTask;
            });

        pending.SetResult();
        await cleanup.ConfigureAwait(true);

        // Assert
        Assert.False(followUpBeforeCompletion);
        Assert.Collection(events,
            entry => Assert.Equal("release", entry),
            entry => Assert.Equal("follow-up", entry));
    }

    /// <summary>Lock-release and command failures cannot prevent follow-up cleanup or replace a primary failure.</summary>
    /// <param name="hasPrimaryFailure">Whether the caller already owns an authoritative failure.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Probe_cleanup_retains_release_pending_and_follow_up_failures(
        bool hasPrimaryFailure
    )
    {
        // Arrange
        var primary = hasPrimaryFailure
            ? new InvalidOperationException("primary")
            : null;

        var releaseFailure = new InvalidOperationException("release");
        var pendingFailure = new InvalidOperationException("pending");
        var followUpFailure = new InvalidOperationException("follow-up");
        var followUpAttempted = false;

        // Act
        var failure = await Record.ExceptionAsync(() => DatabaseLockProbeCleanup.CompleteLockProbeAsync(
            cancellation: null,
            () => Task.FromException(releaseFailure),
            Task.FromException(pendingFailure),
            primary,
            () =>
            {
                followUpAttempted = true;

                return Task.FromException(followUpFailure);
            }));

        // Assert
        Assert.True(followUpAttempted);
        var aggregate = hasPrimaryFailure
            ? Assert.IsType<AggregateException>(primary!.Data[nameof(DatabaseLockProbeCleanup.CompleteLockProbeAsync)])
            : Assert.IsType<AggregateException>(failure);

        Assert.Equal(new[] { releaseFailure, pendingFailure, followUpFailure }, aggregate.InnerExceptions);

        if (hasPrimaryFailure)
        {
            Assert.Null(failure);
        }
    }

    /// <summary>Nested probe teardown retains secondary failures already recorded against the same primary error.</summary>
    [Fact]
    public async Task Probe_cleanup_retains_prior_teardown_failures()
    {
        // Arrange
        var primary = new InvalidOperationException("primary");
        var priorFailure = new InvalidOperationException("inner teardown");
        var currentFailure = new InvalidOperationException("outer teardown");
        primary.Data[nameof(DatabaseLockProbeCleanup.CompleteLockProbeAsync)] = new AggregateException(priorFailure);

        // Act
        var failure = await Record.ExceptionAsync(() => DatabaseLockProbeCleanup.CompleteLockProbeAsync(
            cancellation: null,
            () => Task.CompletedTask,
            Task.FromException(currentFailure),
            primary,
            afterDrain: null));

        // Assert
        Assert.Null(failure);
        var aggregate = Assert.IsType<AggregateException>(
            primary.Data[nameof(DatabaseLockProbeCleanup.CompleteLockProbeAsync)]);

        Assert.Collection(aggregate.InnerExceptions,
            exception => Assert.Same(priorFailure, exception),
            exception => Assert.Same(currentFailure, exception));
    }

    /// <summary>A sole follow-up failure is reported with its original exception identity.</summary>
    [Fact]
    public async Task Probe_cleanup_reports_follow_up_failure_without_an_earlier_failure()
    {
        // Arrange
        var expected = new InvalidOperationException("follow-up");

        // Act
        var failure = await Record.ExceptionAsync(() => DatabaseLockProbeCleanup.CompleteLockProbeAsync(
            cancellation: null,
            () => Task.CompletedTask,
            Task.CompletedTask,
            primaryFailure: null,
            () => Task.FromException(expected)));

        // Assert
        Assert.Same(expected, failure);
    }

    /// <summary>Failure before command start still releases the owner and removes the prepared resource.</summary>
    [Fact]
    public async Task Probe_cleanup_completes_when_no_command_was_started()
    {
        // Arrange
        var expected = new InvalidOperationException("before start");
        var events = new List<string>();

        // Act
        await DatabaseLockProbeCleanup.CompleteLockProbeAsync(
            cancellation: null,
            () =>
            {
                events.Add("release");

                return Task.CompletedTask;
            },
            Task.CompletedTask,
            expected,
            () =>
            {
                events.Add("follow-up");

                return Task.CompletedTask;
            });

        // Assert
        Assert.Collection(events,
            entry => Assert.Equal("release", entry),
            entry => Assert.Equal("follow-up", entry));

        Assert.False(expected.Data.Contains(nameof(DatabaseLockProbeCleanup.CompleteLockProbeAsync)));
    }
}
