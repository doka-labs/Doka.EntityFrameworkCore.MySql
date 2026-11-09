namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Verifies lock-probe failure priority and complete ownership of pending work.</summary>
public sealed partial class MySqlDistributedCacheIntegrationTests
{
    /// <summary>Returns each selected engine with a foreground operation that can wait on a live row.</summary>
    public static IEnumerable<object[]> CleanupObserverFailureCases =>
        SelectedTargets.SelectMany(target => new[]
        {
            new[] { target[0], "refresh" },
            new[] { target[0], "try-get" },
        });

    /// <summary>An observer failure remains visible when protected teardown cancels blocked foreground work.</summary>
    /// <param name="targetName">The selected engine for the isolated lock scenario.</param>
    /// <param name="initiator">The cache operation whose foreground refresh is blocked.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(CleanupObserverFailureCases))]
    public async Task Cleanup_observer_failure_is_not_replaced_by_foreground_cancellation(
        string targetName,
        string initiator
    )
    {
        // Arrange
        await using var store = await CreateDeployedStoreAsync(ParseTarget(targetName), CancellationToken.None);
        await store.InsertExpiredEntriesAsync(2, CancellationToken.None);
        var time = new CacheManualTimeProvider();
        await using var cache = store.CreateCache(time);

        await cache.SetAsync(
            "live",
            [1],
            new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(10) },
            CancellationToken.None);

        await using var blocker = new MySqlConnection(store.ConnectionString);
        await blocker.OpenAsync(CancellationToken.None);
        await using var transaction = await blocker.BeginTransactionAsync(CancellationToken.None);
        await using var locked = new MySqlCommand(
            $"SELECT `Revision` FROM {store.QualifiedTableName} WHERE `Id` = CAST('live' AS BINARY) FOR UPDATE;",
            blocker,
            transaction);

        await locked.ExecuteScalarAsync(CancellationToken.None);
        var expectedFailure = new InvalidOperationException("Injected cleanup observer failure.");
        Task? interruptedOperation = null;

        // Act
        var failure = await Record.ExceptionAsync(() => CancelCleanupWhileBlockedAsync(
            store,
            cache,
            time,
            initiator,
            async (observer, pending, cancellationToken) =>
            {
                interruptedOperation = pending;

                // WHY: The live row blocks the foreground refresh, so teardown-induced cancellation
                // deterministically reproduces the masking path instead of depending on runner scheduling.
                await AssertLockingStatementStartedAsync(observer, store.TableName, pending, cancellationToken);

                throw expectedFailure;
            },
            CancellationToken.None));

        // Assert
        Assert.Same(expectedFailure, failure);
        Assert.NotNull(interruptedOperation);
        Assert.True(interruptedOperation.IsCompleted);

        var secondaryFailures = Assert.IsType<AggregateException>(
            expectedFailure.Data[nameof(CompleteLockProbeAsync)]);

        Assert.IsAssignableFrom<OperationCanceledException>(Assert.Single(secondaryFailures.InnerExceptions));
        Assert.Contains(
            nameof(Cleanup_observer_failure_is_not_replaced_by_foreground_cancellation),
            failure.StackTrace);
        Assert.Equal(2L, await store.CountExpiredAsync(CancellationToken.None));
    }

    /// <summary>Already faulted work is observed even when another error remains authoritative.</summary>
    /// <param name="releaseFails">Whether lock release adds another independently retained failure.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lock_probe_teardown_records_secondary_failures_without_replacing_the_primary(
        bool releaseFails
    )
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var primaryFailure = new InvalidOperationException("Observer failed.");
        var releaseFailure = new InvalidOperationException("Lock release failed.");
        var pendingFailure = new InvalidOperationException("Pending operation failed.");
        var pending = Task.FromException(pendingFailure);
        var released = false;

        // Act
        await CompleteLockProbeAsync(
            cancellation,
            () =>
            {
                released = true;

                return releaseFails ? Task.FromException(releaseFailure) : Task.CompletedTask;
            },
            pending,
            primaryFailure);

        // Assert
        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(released);
        var secondaryFailures = Assert.IsType<AggregateException>(primaryFailure.Data[nameof(CompleteLockProbeAsync)]);
        Assert.Equal(releaseFails ? 2 : 1, secondaryFailures.InnerExceptions.Count);
        Assert.Same(pendingFailure, secondaryFailures.InnerExceptions[^1]);

        if (releaseFails)
        {
            Assert.Same(releaseFailure, secondaryFailures.InnerExceptions[0]);
        }
    }

    /// <summary>A teardown failure is reported when no earlier error exists.</summary>
    [Fact]
    public async Task Lock_probe_teardown_surfaces_a_pending_failure_without_a_primary_error()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var expectedFailure = new InvalidOperationException("Pending operation failed.");
        var pending = Task.FromException(expectedFailure);
        var released = false;

        // Act
        var failure = await Record.ExceptionAsync(() => CompleteLockProbeAsync(
            cancellation,
            () =>
            {
                released = true;

                return Task.CompletedTask;
            },
            pending,
            primaryFailure: null));

        // Assert
        Assert.Same(expectedFailure, failure);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(released);
    }

    /// <summary>Failure while releasing the lock does not prevent pending work from being observed.</summary>
    [Fact]
    public async Task Lock_probe_teardown_reports_release_and_pending_failures()
    {
        // Arrange
        var releaseFailure = new InvalidOperationException("Lock release failed.");
        var pendingFailure = new InvalidOperationException("Pending operation failed.");
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Act
        var failure = await Record.ExceptionAsync(() => CompleteLockProbeAsync(
            cancellation: null,
            () =>
            {
                completion.SetException(pendingFailure);

                return Task.FromException(releaseFailure);
            },
            completion.Task,
            primaryFailure: null));

        // Assert
        var failures = Assert.IsType<AggregateException>(failure);
        Assert.Equal(2, failures.InnerExceptions.Count);
        Assert.Same(releaseFailure, failures.InnerExceptions[0]);
        Assert.Same(pendingFailure, failures.InnerExceptions[1]);
        Assert.True(completion.Task.IsCompleted);
    }

    /// <summary>A throwing cancellation callback cannot skip lock release or task drainage.</summary>
    [Fact]
    public async Task Lock_probe_teardown_finishes_after_a_cancellation_callback_failure()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var expectedFailure = new InvalidOperationException("Cancellation callback failed.");
        using var registration = cancellation.Token.Register(() => throw expectedFailure);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = false;

        // Act
        var failure = await Record.ExceptionAsync(() => CompleteLockProbeAsync(
            cancellation,
            () =>
            {
                released = true;
                completion.SetResult();

                return Task.CompletedTask;
            },
            completion.Task,
            primaryFailure: null));

        // Assert
        var failures = Assert.IsType<AggregateException>(failure);
        Assert.Same(expectedFailure, Assert.Single(failures.InnerExceptions));
        Assert.True(released);
        Assert.True(completion.Task.IsCompletedSuccessfully);
    }

    /// <summary>Observing the original task failure does not create self-referential diagnostic data.</summary>
    [Fact]
    public async Task Lock_probe_teardown_does_not_attach_the_primary_failure_to_itself()
    {
        // Arrange
        var primaryFailure = new InvalidOperationException("Pending operation failed.");
        var pending = Task.FromException(primaryFailure);

        // Act
        await CompleteLockProbeAsync(
            cancellation: null,
            () => Task.CompletedTask,
            pending,
            primaryFailure);

        // Assert
        Assert.False(primaryFailure.Data.Contains(nameof(CompleteLockProbeAsync)));
        Assert.True(pending.IsCompleted);
    }
}
