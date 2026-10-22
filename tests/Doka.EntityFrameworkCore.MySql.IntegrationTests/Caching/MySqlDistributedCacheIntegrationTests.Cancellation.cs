namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Verifies blocked foreground operations and cancellation recovery.</summary>
public sealed partial class MySqlDistributedCacheIntegrationTests
{
    /// <summary>Verifies that one lock-waiting operation cannot resurrect an expired entry.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(WaitedRefreshCases))]
    public async Task Waited_refresh_does_not_resurrect_expired_entries(
        string targetName,
        string operation
    )
    {
        // Arrange
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        const string key = "expired-during-lock-wait";

        await using var lockConnection = new MySqlConnection(store.ConnectionString);

        await lockConnection
            .OpenAsync(CancellationToken.None)
            .ConfigureAwait(false);

        await using var observer = new MySqlConnection(store.ConnectionString);

        await observer
            .OpenAsync(CancellationToken.None)
            .ConfigureAwait(false);

        await store
            .Cache
            .SetAsync(key, [1], new DistributedCacheEntryOptions(), CancellationToken.None)
            .ConfigureAwait(false);

        await using var transaction = await lockConnection
            .BeginTransactionAsync(CancellationToken.None)
            .ConfigureAwait(false);

        await using (var locked = new MySqlCommand(
                         $"SELECT `Revision` FROM {store.QualifiedTableName} "
                         + "WHERE `Id` = CAST(@key AS BINARY) FOR UPDATE;",
                         lockConnection,
                         transaction))
        {
            locked.Parameters.AddWithValue("@key", key);

            await locked
                .ExecuteScalarAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }

        // Act
        var pending = operation switch
        {
            // WHY: These variants must exercise blocking synchronous cache calls on worker threads.
            // ReSharper disable once MethodHasAsyncOverload
            "refresh-sync" => Task.Run(() => store.Cache.Refresh(key), CancellationToken.None),
            "refresh-async" => store.Cache.RefreshAsync(key, CancellationToken.None),
            // WHY: This lock-wait variant exercises synchronous Get on a worker thread.
            // ReSharper disable once MethodHasAsyncOverload
            "get-sync" => Task.Run(() => store.Cache.Get(key), CancellationToken.None),
            "get-async" => store.Cache.GetAsync(key, CancellationToken.None),
            // WHY: This lock-wait variant exercises synchronous TryGet on a worker thread.
            // ReSharper disable once MethodHasAsyncOverload
            "try-get-sync" => Task.Run(() => store.Cache.TryGet(key, new ExactBufferWriter(1)), CancellationToken.None),
            "try-get-async" => store
                .Cache
                .TryGetAsync(key, new ExactBufferWriter(1), CancellationToken.None)
                .AsTask(),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        try
        {
            await AssertLockingStatementStartedAsync(observer, store.TableName, pending)
                .ConfigureAwait(false);

            await using var expire = new MySqlCommand(
                $"UPDATE {store.QualifiedTableName} SET `ExpiresAtUtc` = UTC_TIMESTAMP(6) "
                + "WHERE `Id` = CAST(@key AS BINARY);",
                lockConnection,
                transaction);

            expire.Parameters.AddWithValue("@key", key);

            await expire
                .ExecuteNonQueryAsync(CancellationToken.None)
                .ConfigureAwait(false);

            await transaction
                .CommitAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
            await transaction
                .RollbackAsync(CancellationToken.None)
                .ConfigureAwait(false);

            await pending
                .WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None)
                .ConfigureAwait(false);

            throw;
        }

        await pending
            .WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None)
            .ConfigureAwait(false);

        var entry = await store
            .ReadEntryAsync(key)
            .ConfigureAwait(false);

        var now = await store
            .ReadUtcNowAsync()
            .ConfigureAwait(false);

        // Assert
        Assert.True(
            entry.ExpiresAt <= now,
            "A refresh that waited for a row lock extended a row that expired before the lock was released.");

        Assert.Null(
            await store
                .Cache
                .GetAsync(key, CancellationToken.None)
                .ConfigureAwait(false));
    }

    /// <summary>Verifies one sliding-read lock outcome and subsequent pool recovery.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SlidingLockWaitCases))]
    public async Task Sliding_lock_waits_complete_or_cancel_without_pool_loss(
        string targetName,
        bool useTryGet,
        bool cancel
    )
    {
        // Arrange
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        const string key = "sliding-refresh-lock-wait";
        var connectionString = new MySqlConnectionStringBuilder(store.ConnectionString)
        {
            MaximumPoolSize = 1,
        }.ConnectionString;

        await using var cache = store.CreateCache(new CacheManualTimeProvider(), connectionString: connectionString);
        await using var lockConnection = new MySqlConnection(store.ConnectionString);

        await lockConnection
            .OpenAsync(CancellationToken.None)
            .ConfigureAwait(false);

        await using var observer = new MySqlConnection(store.ConnectionString);

        await observer
            .OpenAsync(CancellationToken.None)
            .ConfigureAwait(false);

        await cache
            .SetAsync(key, [1], new DistributedCacheEntryOptions(), CancellationToken.None)
            .ConfigureAwait(false);

        await store
            .ExecuteForKeyAsync(
                $"UPDATE {store.QualifiedTableName} "
                + "SET `ExpiresAtUtc` = TIMESTAMPADD(SECOND, 30, UTC_TIMESTAMP(6)) "
                + "WHERE `Id` = CAST(@key AS BINARY);",
                key)
            .ConfigureAwait(false);

        var before = await store
            .ReadEntryAsync(key)
            .ConfigureAwait(false);

        await using var transaction = await lockConnection
            .BeginTransactionAsync(CancellationToken.None)
            .ConfigureAwait(false);

        await using (var locked = new MySqlCommand(
                         $"SELECT `Revision` FROM {store.QualifiedTableName} "
                         + "WHERE `Id` = CAST(@key AS BINARY) FOR UPDATE;",
                         lockConnection,
                         transaction))
        {
            locked.Parameters.AddWithValue("@key", key);

            await locked
                .ExecuteScalarAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var destination = new ExactBufferWriter(1);
        Exception? failure = null;
        var found = false;
        byte[]? value = null;
        CacheIntegrationStore.EntryState after;
        byte[]? recoveryMiss;

        // Act
        // WHY: The lock wait, chosen completion outcome, and pool reuse form one
        // lifecycle; a fresh cache would hide a leaked single-pool connection.
        Task pending = useTryGet
            ? cache
                .TryGetAsync(key, destination, cancellation.Token)
                .AsTask()
            : cache.GetAsync(key, cancellation.Token);
        var released = false;

        try
        {
            await AssertLockingStatementStartedAsync(observer, store.TableName, pending)
                .ConfigureAwait(false);

            if (cancel)
            {
                await cancellation
                    .CancelAsync()
                    .ConfigureAwait(false);

                failure = await Record
                    .ExceptionAsync(() => pending.WaitAsync(
                        TimeSpan.FromSeconds(15),
                        CancellationToken.None))
                    .ConfigureAwait(false);
            }
            else
            {
                await transaction
                    .CommitAsync(CancellationToken.None)
                    .ConfigureAwait(false);

                released = true;

                await pending
                    .WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None)
                    .ConfigureAwait(false);

                if (useTryGet)
                {
                    found = await ((Task<bool>)pending).ConfigureAwait(false);
                }
                else
                {
                    value = await ((Task<byte[]?>)pending).ConfigureAwait(false);
                }
            }

            after = await store.ReadEntryAsync(key).ConfigureAwait(false);
            recoveryMiss = await cache
                .GetAsync("single-pool-recovered-miss", CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            await cancellation
                .CancelAsync()
                .ConfigureAwait(false);

            if (!released)
            {
                await transaction
                    .RollbackAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }

            if (!pending.IsCompleted)
            {
                // WHY: This teardown checkpoint must drain unfinished work before
                // the pooled cache is disposed, even when an earlier step failed.
                await Assert
                    .ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(
                        TimeSpan.FromSeconds(15),
                        CancellationToken.None))
                    .ConfigureAwait(false);
            }
        }

        // Assert
        Assert.Null(recoveryMiss);

        if (cancel)
        {
            Assert.IsAssignableFrom<OperationCanceledException>(failure);
            Assert.Equal(before, after);
        }
        else
        {
            Assert.Null(failure);

            if (useTryGet)
            {
                Assert.True(found);
                Assert.Equal(new byte[] { 1 }, destination.WrittenBytes);
            }
            else
            {
                Assert.Equal(new byte[] { 1 }, value);
            }

            Assert.Equal(before.Revision, after.Revision);
            Assert.True(after.ExpiresAt > before.ExpiresAt);
        }
    }

    /// <summary>Verifies cancellation of one public cache operation under a table lock.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(LockedCancellationCases))]
    public async Task Locked_cache_operations_observe_cancellation(
        string targetName,
        string initiator
    )
    {
        // Arrange
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        var connectionString = new MySqlConnectionStringBuilder(store.ConnectionString)
        {
            MaximumPoolSize = 1,
            ConnectionTimeout = 3,
        }.ConnectionString;

        await using var cache = store.CreateCache(new CacheManualTimeProvider(), connectionString: connectionString);

        await cache
            .SetAsync("cancelled", [1], new DistributedCacheEntryOptions(), CancellationToken.None)
            .ConfigureAwait(false);

        await using var observer = new MySqlConnection(store.ConnectionString);

        await observer
            .OpenAsync(CancellationToken.None)
            .ConfigureAwait(false);

        using var cancellation = new CancellationTokenSource();
        Task? pending = null;

        await store
            .ExecuteAsync($"LOCK TABLES {store.QualifiedTableName} WRITE;")
            .ConfigureAwait(false);

        try
        {
            // Act
            // WHY: The canceled operation and subsequent read share the single
            // pooled connection to prove that cancellation returns its lease.
            pending = InvokeLockedCacheOperation(cache, initiator, cancellation.Token);

            await AssertWaitingForTableLockAsync(observer, store.TableName, pending)
                .ConfigureAwait(false);

            await cancellation
                .CancelAsync()
                .ConfigureAwait(false);

            // Assert
            await Assert
                .ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(
                    TimeSpan.FromSeconds(15),
                    CancellationToken.None))
                .ConfigureAwait(false);
        }
        finally
        {
            await cancellation
                .CancelAsync()
                .ConfigureAwait(false);

            await store
                .ExecuteAsync("UNLOCK TABLES;")
                .ConfigureAwait(false);

            if (pending is not null
                && !pending.IsCompleted)
            {
                await Assert
                    .ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(
                        TimeSpan.FromSeconds(15),
                        CancellationToken.None))
                    .ConfigureAwait(false);
            }
        }

        Assert.Equal(
            new byte[] { 1 },
            await cache
                .GetAsync("cancelled", CancellationToken.None)
                .ConfigureAwait(false));
    }

    /// <summary>Starts one cancellation-aware cache API while its table is locked.</summary>
    private static Task InvokeLockedCacheOperation(
        MySqlDistributedCache cache,
        string initiator,
        CancellationToken cancellationToken
    )
    {
        switch (initiator)
        {
            case "get":
                return cache.GetAsync("cancelled", cancellationToken);

            case "try-get":
                return cache
                    .TryGetAsync("cancelled", new ExactBufferWriter(1), cancellationToken)
                    .AsTask();

            case "set-array":
                return cache.SetAsync(
                    "cancelled",
                    [2],
                    new DistributedCacheEntryOptions(),
                    cancellationToken);

            case "set-sequence":
                return cache
                    .SetAsync(
                        "cancelled",
                        new ReadOnlySequence<byte>([3]),
                        new DistributedCacheEntryOptions(),
                        cancellationToken)
                    .AsTask();

            case "set-segmented":
                var first = new CacheSequenceSegment(new byte[] { 3 });
                var last = first.Append(new byte[] { 4 });
                var segmented = new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);

                return cache
                    .SetAsync("cancelled", segmented, new DistributedCacheEntryOptions(), cancellationToken)
                    .AsTask();

            case "refresh":
                return cache.RefreshAsync("cancelled", cancellationToken);

            case "remove":
                return cache.RemoveAsync("cancelled", cancellationToken);

            default:
                throw new ArgumentOutOfRangeException(nameof(initiator));
        }
    }
}
