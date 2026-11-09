namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Verifies bounded cleanup scheduling and cancellation recovery.</summary>
public sealed partial class MySqlDistributedCacheIntegrationTests
{
    /// <summary>Verifies the cleanup interval, 1,000-row bound, and continuation.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Cleanup_observes_interval_and_batch_limit(
        string targetName
    )
    {
        // Arrange
        await using var store = await CreateDeployedStoreAsync(ParseTarget(targetName), CancellationToken.None)
            .ConfigureAwait(false);

        await store
            .InsertExpiredEntriesAsync(1005, CancellationToken.None)
            .ConfigureAwait(false);

        var time = new CacheManualTimeProvider();
        var logger = new CacheRecordingLogger();
        await using var cache = store.CreateCache(time, logger);

        await cache
            .SetAsync("live", [1], new DistributedCacheEntryOptions(), CancellationToken.None)
            .ConfigureAwait(false);

        var initialCount = await store
            .CountExpiredAsync(CancellationToken.None)
            .ConfigureAwait(false);

        // Act
        // WHY: Interval, bounded batches, immediate continuation, and cooldown
        // are one scheduler lifecycle on the same cache instance and clock.
        // WHY: A synchronous miss must leave expired rows untouched before the cleanup interval is due.
        // ReSharper disable once MethodHasAsyncOverload
        var beforeIntervalMiss = cache.Get("cleanup-miss");
        var beforeIntervalCount = await store
            .CountExpiredAsync(CancellationToken.None)
            .ConfigureAwait(false);

        time.Advance(TimeSpan.FromMinutes(5));

        // WHY: Verify the synchronous 1,000-row cleanup bound before exercising the asynchronous path.
        // ReSharper disable once MethodHasAsyncOverload
        var firstBatchMiss = cache.Get("cleanup-miss");
        var firstBatchCount = await store
            .CountExpiredAsync(CancellationToken.None)
            .ConfigureAwait(false);

        var continuationMiss = await cache
            .GetAsync("cleanup-miss", CancellationToken.None)
            .ConfigureAwait(false);

        var continuationCount = await store
            .CountExpiredAsync(CancellationToken.None)
            .ConfigureAwait(false);

        await store
            .InsertExpiredEntriesAsync(2, CancellationToken.None)
            .ConfigureAwait(false);

        var cooldownMiss = await cache
            .GetAsync("cleanup-miss", CancellationToken.None)
            .ConfigureAwait(false);

        var cooldownCount = await store
            .CountExpiredAsync(CancellationToken.None)
            .ConfigureAwait(false);

        time.Advance(TimeSpan.FromMinutes(5));

        var nextIntervalMiss = await cache
            .GetAsync("cleanup-miss", CancellationToken.None)
            .ConfigureAwait(false);

        var nextIntervalCount = await store
            .CountExpiredAsync(CancellationToken.None)
            .ConfigureAwait(false);

        var liveValue = await cache
            .GetAsync("live", CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        Assert.Equal(1005L, initialCount);
        Assert.Null(beforeIntervalMiss);
        Assert.Equal(1005L, beforeIntervalCount);
        Assert.Null(firstBatchMiss);
        Assert.Equal(5L, firstBatchCount);
        Assert.Null(continuationMiss);
        Assert.Equal(0L, continuationCount);
        Assert.Null(cooldownMiss);
        Assert.Equal(2L, cooldownCount);
        Assert.Null(nextIntervalMiss);
        Assert.Equal(0L, nextIntervalCount);
        Assert.Equal(new byte[] { 1 }, liveValue);
    }

    /// <summary>Verifies one canceled cleanup initiator and its subsequent recovery.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(CleanupCancellationCases))]
    public async Task Canceled_cleanup_recovers_for_initiator(
        string targetName,
        string initiator
    )
    {
        // Arrange
        var target = ParseTarget(targetName);
        await using var store = await CreateDeployedStoreAsync(target, CancellationToken.None)
            .ConfigureAwait(false);

        await store
            .InsertExpiredEntriesAsync(2, CancellationToken.None)
            .ConfigureAwait(false);

        var time = new CacheManualTimeProvider();
        var logger = new CacheRecordingLogger();
        var cleanupConnectionString = new MySqlConnectionStringBuilder(store.ConnectionString)
        {
            AutoEnlist = false,
            MaximumPoolSize = CleanupProbeConnectionCount,
        }.ConnectionString;

        await using var cleanupDataSource = new MySqlDataSource(cleanupConnectionString);

        await WarmCleanupProbePoolAsync(cleanupDataSource, CancellationToken.None)
            .ConfigureAwait(false);

        await using var cache = store.CreateCache(time, cleanupDataSource, logger);

        await cache
            .SetAsync("live", [1], new DistributedCacheEntryOptions(), CancellationToken.None)
            .ConfigureAwait(false);

        if (initiator == "remove")
        {
            await cache
                .SetAsync("cleanup-array", [2], new DistributedCacheEntryOptions(), CancellationToken.None)
                .ConfigureAwait(false);
        }

        await store
            .ExecuteForKeyAsync(
                $"UPDATE {store.QualifiedTableName} SET `ExpiresAtUtc` = TIMESTAMPADD(SECOND, -2, UTC_TIMESTAMP(6)) "
                + "WHERE `Id` = CAST(@key AS BINARY);",
                "expired-0",
                CancellationToken.None)
            .ConfigureAwait(false);

        // Act
        // WHY: The canceled cleanup and its next scheduled attempt must share
        // one active-cleanup state and clock to prove recovery after cancellation.
        var result = await CancelCleanupWhileBlockedAsync(store, cache, time, initiator, CancellationToken.None)
            .ConfigureAwait(false);

        var entriesBeforeRecovery = logger.Entries.ToArray();
        var liveValue = await cache
            .GetAsync("live", CancellationToken.None)
            .ConfigureAwait(false);

        var expiredBeforeRecovery = await store
            .CountExpiredAsync(CancellationToken.None)
            .ConfigureAwait(false);

        var initiatorValue = await ReadCleanupInitiatorResultAsync(cache, initiator, result, CancellationToken.None)
            .ConfigureAwait(false);

        var expectedValue = initiator switch
        {
            "get" or "try-get" => new byte[] { 1 },
            "set-array" => new byte[] { 2 },
            "set-sequence" => new byte[] { 3 },
            "set-segmented" => new byte[] { 3, 4 },
            _ => null,
        };

        time.Advance(TimeSpan.FromMinutes(5));

        // WHY: Recovery is verified with a small blocked batch; the separate bound test covers full batches.
        // ReSharper disable once MethodHasAsyncOverload
        var recoveryMiss = cache.Get("cleanup-miss");
        var expiredAfterRecovery = await store
            .CountExpiredAsync(CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        Assert.Empty(entriesBeforeRecovery);
        Assert.Equal(new byte[] { 1 }, liveValue);
        Assert.Equal(2L, expiredBeforeRecovery);
        Assert.Equal(expectedValue, initiatorValue);
        Assert.Null(recoveryMiss);
        Assert.Equal(0L, expiredAfterRecovery);
    }

    /// <summary>Coordinates a blocked cleanup, foreground probe, cancellation, and lock release.</summary>
    /// <param name="store">The store used by this operation.</param>
    /// <param name="cache">The cache used by this operation.</param>
    /// <param name="time">The time used by this operation.</param>
    /// <param name="initiator">The initiator used by this operation.</param>
    /// <param name="cancellationToken">The token to observe during asynchronous work.</param>
    private static Task<byte[]?> CancelCleanupWhileBlockedAsync(
        CacheIntegrationStore store,
        MySqlDistributedCache cache,
        CacheManualTimeProvider time,
        string initiator,
        CancellationToken cancellationToken
    ) => CancelCleanupWhileBlockedAsync(
        store,
        cache,
        time,
        initiator,
        (observer, pending, observationCancellationToken) => AssertLockingStatementStartedAsync(
            observer,
            store.TableName,
            pending,
            observationCancellationToken,
            cleanupOnly: true),
        cancellationToken);

    /// <summary>Runs the real cleanup lifecycle with a replaceable observation boundary for failure tests.</summary>
    /// <param name="store">The isolated cache deployment and lock owner.</param>
    /// <param name="cache">The cache whose cleanup and recovery state are exercised.</param>
    /// <param name="time">The cache clock used to advance the cleanup interval.</param>
    /// <param name="initiator">The public foreground operation that triggers cleanup.</param>
    /// <param name="observeCleanup">The observation boundary that confirms progress or reports a failure.</param>
    /// <param name="cancellationToken">The token observed while arranging and observing the operation.</param>
    private static async Task<byte[]?> CancelCleanupWhileBlockedAsync(
        CacheIntegrationStore store,
        MySqlDistributedCache cache,
        CacheManualTimeProvider time,
        string initiator,
        Func<MySqlConnection, Task, CancellationToken, Task> observeCleanup,
        CancellationToken cancellationToken
    )
    {
        await using var lockConnection = new MySqlConnection(store.ConnectionString);

        await lockConnection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var observer = new MySqlConnection(store.ConnectionString);

        await observer
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var transaction = await lockConnection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using (var locked = new MySqlCommand(
            $"SELECT `Revision` FROM {store.QualifiedTableName} "
            + "WHERE `Id` = CAST('expired-0' AS BINARY) FOR UPDATE;",
            lockConnection,
            transaction))
        {
            var lockedRevision = await locked
                .ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false);

            Assert.Equal(0L, Assert.IsType<long>(lockedRevision));
        }

        using var cancellation = new CancellationTokenSource();
        time.Advance(TimeSpan.FromMinutes(5));
        var pending = InvokeCleanupInitiatorAsync(cache, initiator, cancellation.Token);
        Exception? primaryFailure = null;

        try
        {
            await observeCleanup(observer, pending, cancellationToken)
                .ConfigureAwait(false);

            if (initiator == "get")
            {
                // WHY: The isolated pool makes this a cleanup-coordination assertion, not a pool-capacity test.
                Assert.Null(
                    await cache
                        .GetAsync("cleanup-miss", cancellationToken)
                        .WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None)
                        .ConfigureAwait(false));
            }

            Assert.False(pending.IsCompleted);

            // WHY: The retry interval starts when the canceled batch finishes, not when it starts.
            time.Advance(TimeSpan.FromMinutes(5));

            await cancellation
                .CancelAsync()
                .ConfigureAwait(false);

            return await pending
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            primaryFailure = exception;

            throw;
        }
        finally
        {
            // WHY: Observation may fail before foreground work finishes. Cancel and unlock before
            // draining it, retaining the observer failure instead of replacing it with induced cancellation.
            await CompleteLockProbeAsync(
                    cancellation,
                    () => transaction.RollbackAsync(CancellationToken.None),
                    pending,
                    primaryFailure)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Releases a probe's lock and observes all owned work without masking its primary failure.</summary>
    /// <param name="cancellation">The cancellation source, or null when unlocking completes the work.</param>
    /// <param name="releaseLock">The protected asynchronous release of the probe-owned database lock.</param>
    /// <param name="pending">The operation that must finish before its resources can be disposed.</param>
    /// <param name="primaryFailure">The existing failure that remains authoritative over teardown failures.</param>
    private static async Task CompleteLockProbeAsync(
        CancellationTokenSource? cancellation,
        Func<Task> releaseLock,
        Task pending,
        Exception? primaryFailure
    )
    {
        // WHY: Cancellation, lock release, and task drainage must all finish even if one fails.
        // This ownership boundary deliberately has no cancellation token or abandonable timed wait.
        List<Exception>? failures = null;

        try
        {
            if (cancellation is not null)
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        try
        {
            await releaseLock().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        try
        {
            // WHY: IsCompleted also includes faulted and canceled tasks, which still require observation.
            await pending.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (!ReferenceEquals(exception, primaryFailure))
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is null)
        {
            return;
        }

        if (primaryFailure is not null)
        {
            // WHY: Keep secondary failures available without changing the original exception identity or stack.
            primaryFailure.Data[nameof(CompleteLockProbeAsync)] = new AggregateException(failures);

            return;
        }

        if (failures.Count == 1)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        throw new AggregateException("Lock probe teardown failed.", failures);
    }

    /// <summary>Starts the selected foreground operation that initiates opportunistic cleanup.</summary>
    private static async Task<byte[]?> InvokeCleanupInitiatorAsync(
        MySqlDistributedCache cache,
        string initiator,
        CancellationToken cancellationToken
    )
    {
        switch (initiator)
        {
            case "get":
                return await cache
                    .GetAsync("live", cancellationToken)
                    .ConfigureAwait(false);

            case "try-get":
                var destination = new ExactBufferWriter(1);
                var found = await cache
                    .TryGetAsync("live", destination, cancellationToken)
                    .ConfigureAwait(false);

                return found
                    ? destination.WrittenBytes
                    : null;

            case "set-array":
                await cache
                    .SetAsync("cleanup-array", [2], new DistributedCacheEntryOptions(), cancellationToken)
                    .ConfigureAwait(false);

                return null;

            case "set-sequence":
                await cache
                    .SetAsync(
                        "cleanup-sequence",
                        new ReadOnlySequence<byte>([3]),
                        new DistributedCacheEntryOptions(),
                        cancellationToken)
                    .ConfigureAwait(false);

                return null;

            case "set-segmented":
                var first = new CacheSequenceSegment(new byte[] { 3 });
                var last = first.Append(new byte[] { 4 });
                var segmented = new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);

                await cache
                    .SetAsync("cleanup-segmented", segmented, new DistributedCacheEntryOptions(), cancellationToken)
                    .ConfigureAwait(false);

                return null;

            case "refresh":
                await cache
                    .RefreshAsync("live", cancellationToken)
                    .ConfigureAwait(false);

                return null;

            case "remove":
                await cache
                    .RemoveAsync("cleanup-array", cancellationToken)
                    .ConfigureAwait(false);

                return null;

            default:
                throw new ArgumentOutOfRangeException(nameof(initiator));
        }
    }

    /// <summary>Reads the foreground result without triggering another scheduled cleanup.</summary>
    /// <param name="cache">The cache used by this operation.</param>
    /// <param name="initiator">The initiator used by this operation.</param>
    /// <param name="result">The result used by this operation.</param>
    /// <param name="cancellationToken">The token to observe during asynchronous work.</param>
    private static async Task<byte[]?> ReadCleanupInitiatorResultAsync(
        MySqlDistributedCache cache,
        string initiator,
        byte[]? result,
        CancellationToken cancellationToken
    )
    {
        return initiator switch
        {
            "get" or "try-get" or "refresh" => result,
            "set-array" or "remove" => await cache
                .GetAsync("cleanup-array", cancellationToken)
                .ConfigureAwait(false),
            "set-sequence" => await cache
                .GetAsync("cleanup-sequence", cancellationToken)
                .ConfigureAwait(false),
            "set-segmented" => await cache
                .GetAsync("cleanup-segmented", cancellationToken)
                .ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(initiator)),
        };
    }

    /// <summary>Primes both leases so lock-observer timing excludes connection startup.</summary>
    /// <param name="dataSource">The data source used by this operation.</param>
    /// <param name="cancellationToken">The token to observe during asynchronous work.</param>
    private static async Task WarmCleanupProbePoolAsync(
        MySqlDataSource dataSource,
        CancellationToken cancellationToken
    )
    {
        await using var cleanupConnection = await dataSource
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var probeConnection = await dataSource
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
