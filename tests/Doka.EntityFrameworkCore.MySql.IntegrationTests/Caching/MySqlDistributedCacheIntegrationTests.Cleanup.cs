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
        await using var store = await CreateDeployedStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        await store
            .InsertExpiredEntriesAsync(1005)
            .ConfigureAwait(false);

        var time = new CacheManualTimeProvider();
        var logger = new CacheRecordingLogger();
        await using var cache = store.CreateCache(time, logger);

        await cache
            .SetAsync("live", [1], new DistributedCacheEntryOptions(), CancellationToken.None)
            .ConfigureAwait(false);

        var initialCount = await store.CountExpiredAsync().ConfigureAwait(false);

        // Act
        // WHY: Interval, bounded batches, immediate continuation, and cooldown
        // are one scheduler lifecycle on the same cache instance and clock.
        // WHY: A synchronous miss must leave expired rows untouched before the cleanup interval is due.
        // ReSharper disable once MethodHasAsyncOverload
        var beforeIntervalMiss = cache.Get("cleanup-miss");
        var beforeIntervalCount = await store.CountExpiredAsync().ConfigureAwait(false);

        time.Advance(TimeSpan.FromMinutes(5));

        // WHY: Verify the synchronous 1,000-row cleanup bound before exercising the asynchronous path.
        // ReSharper disable once MethodHasAsyncOverload
        var firstBatchMiss = cache.Get("cleanup-miss");
        var firstBatchCount = await store.CountExpiredAsync().ConfigureAwait(false);
        var continuationMiss = await cache.GetAsync("cleanup-miss", CancellationToken.None).ConfigureAwait(false);
        var continuationCount = await store.CountExpiredAsync().ConfigureAwait(false);

        await store
            .InsertExpiredEntriesAsync(2)
            .ConfigureAwait(false);

        var cooldownMiss = await cache.GetAsync("cleanup-miss", CancellationToken.None).ConfigureAwait(false);
        var cooldownCount = await store.CountExpiredAsync().ConfigureAwait(false);

        time.Advance(TimeSpan.FromMinutes(5));

        var nextIntervalMiss = await cache.GetAsync("cleanup-miss", CancellationToken.None).ConfigureAwait(false);
        var nextIntervalCount = await store.CountExpiredAsync().ConfigureAwait(false);
        var liveValue = await cache.GetAsync("live", CancellationToken.None).ConfigureAwait(false);

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
        await using var store = await CreateDeployedStoreAsync(target)
            .ConfigureAwait(false);

        await store
            .InsertExpiredEntriesAsync(2)
            .ConfigureAwait(false);

        var time = new CacheManualTimeProvider();
        var logger = new CacheRecordingLogger();
        var cleanupConnectionString = new MySqlConnectionStringBuilder(store.ConnectionString)
        {
            AutoEnlist = false,
            MaximumPoolSize = CleanupProbeConnectionCount,
        }.ConnectionString;

        await using var cleanupDataSource = new MySqlDataSource(cleanupConnectionString);

        await WarmCleanupProbePoolAsync(cleanupDataSource)
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
                "expired-0")
            .ConfigureAwait(false);

        // Act
        // WHY: The canceled cleanup and its next scheduled attempt must share
        // one active-cleanup state and clock to prove recovery after cancellation.
        var result = await CancelCleanupWhileBlockedAsync(store, cache, time, initiator)
            .ConfigureAwait(false);
        var entriesBeforeRecovery = logger.Entries.ToArray();
        var liveValue = await cache.GetAsync("live", CancellationToken.None).ConfigureAwait(false);
        var expiredBeforeRecovery = await store.CountExpiredAsync().ConfigureAwait(false);
        var initiatorValue = await ReadCleanupInitiatorResultAsync(cache, initiator, result)
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
        var expiredAfterRecovery = await store.CountExpiredAsync().ConfigureAwait(false);

        // Assert
        Assert.Empty(entriesBeforeRecovery);
        Assert.Equal(new byte[] { 1 }, liveValue);
        Assert.Equal(2L, expiredBeforeRecovery);
        Assert.Equal(expectedValue, initiatorValue);
        Assert.Null(recoveryMiss);
        Assert.Equal(0L, expiredAfterRecovery);
    }

    /// <summary>Coordinates a blocked cleanup, foreground probe, cancellation, and lock release.</summary>
    private static async Task<byte[]?> CancelCleanupWhileBlockedAsync(
        CacheIntegrationStore store,
        MySqlDistributedCache cache,
        CacheManualTimeProvider time,
        string initiator
    )
    {
        await using var lockConnection = new MySqlConnection(store.ConnectionString);

        await lockConnection
            .OpenAsync(CancellationToken.None)
            .ConfigureAwait(false);

        await using var observer = new MySqlConnection(store.ConnectionString);

        await observer
            .OpenAsync(CancellationToken.None)
            .ConfigureAwait(false);

        await using var transaction = await lockConnection
            .BeginTransactionAsync(CancellationToken.None)
            .ConfigureAwait(false);

        await using (var locked = new MySqlCommand(
                         $"SELECT `Revision` FROM {store.QualifiedTableName} "
                         + "WHERE `Id` = CAST('expired-0' AS BINARY) FOR UPDATE;",
                         lockConnection,
                         transaction))
        {
            var lockedRevision = await locked
                .ExecuteScalarAsync(CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Equal(0L, Assert.IsType<long>(lockedRevision));
        }

        using var cancellation = new CancellationTokenSource();
        time.Advance(TimeSpan.FromMinutes(5));
        var pending = InvokeCleanupInitiatorAsync(cache, initiator, cancellation.Token);

        try
        {
            await AssertLockingStatementStartedAsync(observer, store.TableName, pending, cleanupOnly: true)
                .ConfigureAwait(false);

            if (initiator == "get")
            {
                // WHY: The isolated pool makes this a cleanup-coordination assertion, not a pool-capacity test.
                Assert.Null(
                    await cache
                        .GetAsync("cleanup-miss", CancellationToken.None)
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
                .WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            await cancellation
                .CancelAsync()
                .ConfigureAwait(false);

            await transaction
                .RollbackAsync(CancellationToken.None)
                .ConfigureAwait(false);

            if (!pending.IsCompleted)
            {
                await pending
                    .WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
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

                return found ? destination.WrittenBytes : null;

            case "set-array":
                await cache
                    .SetAsync("cleanup-array", [2], new DistributedCacheEntryOptions(), cancellationToken)
                    .ConfigureAwait(false);

                return null;

            case "set-sequence":
                await cache
                    .SetAsync(
                        "cleanup-sequence",
                        new ReadOnlySequence<byte>(new byte[] { 3 }),
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
    private static async Task<byte[]?> ReadCleanupInitiatorResultAsync(
        MySqlDistributedCache cache,
        string initiator,
        byte[]? result
    )
    {
        return initiator switch
        {
            "get" or "try-get" or "refresh" => result,
            "set-array" or "remove" => await cache.GetAsync("cleanup-array", CancellationToken.None)
                .ConfigureAwait(false),
            "set-sequence" => await cache.GetAsync("cleanup-sequence", CancellationToken.None)
                .ConfigureAwait(false),
            "set-segmented" => await cache.GetAsync("cleanup-segmented", CancellationToken.None)
                .ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(initiator)),
        };
    }

    /// <summary>Primes both leases so lock-observer timing excludes connection startup.</summary>
    private static async Task WarmCleanupProbePoolAsync(
        MySqlDataSource dataSource
    )
    {
        await using var cleanupConnection = await dataSource
            .OpenConnectionAsync(CancellationToken.None)
            .ConfigureAwait(false);

        await using var probeConnection = await dataSource
            .OpenConnectionAsync(CancellationToken.None)
            .ConfigureAwait(false);
    }
}
