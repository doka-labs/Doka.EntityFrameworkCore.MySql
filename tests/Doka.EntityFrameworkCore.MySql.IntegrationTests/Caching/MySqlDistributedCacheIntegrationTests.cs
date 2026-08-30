namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

[Collection(IntegrationDatabaseTestGroup.Name)]
public sealed class MySqlDistributedCacheIntegrationTests
{
    private const int CleanupProbeConnectionCount = 2;
    private const int ConcurrentUpsertProbeCount = 16;

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task MySql84_preserves_distributed_cache_contracts() =>
        AssertCacheContractsAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task MySql97_preserves_distributed_cache_contracts() =>
        AssertCacheContractsAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task MariaDb1011_preserves_distributed_cache_contracts() =>
        AssertCacheContractsAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task MariaDb114_preserves_distributed_cache_contracts() =>
        AssertCacheContractsAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDb118_preserves_distributed_cache_contracts() =>
        AssertCacheContractsAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task MariaDb123_preserves_distributed_cache_contracts() =>
        AssertCacheContractsAsync(IntegrationDatabaseTarget.MariaDb123);

    private static async Task AssertCacheContractsAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using var store = await CacheIntegrationStore
            .CreateAsync(target)
            .ConfigureAwait(false);

        await AssertExplicitIdempotentDeploymentAsync(store)
            .ConfigureAwait(false);

        await AssertQuotedTableIdentifierAsync(store)
            .ConfigureAwait(false);

        await AssertDeploymentTableIsolationAsync(store, target)
            .ConfigureAwait(false);

        await AssertByteAndBufferContractsAsync(store)
            .ConfigureAwait(false);

        await AssertOrdinalKeysAsync(store)
            .ConfigureAwait(false);

        await AssertExpirationContractsAsync(store)
            .ConfigureAwait(false);

        await AssertConcurrentUpsertsAsync(store)
            .ConfigureAwait(false);

        await AssertReadRefreshRacesAsync(store, useAsync: false)
            .ConfigureAwait(false);

        await AssertReadRefreshRacesAsync(store, useAsync: true)
            .ConfigureAwait(false);

        await AssertAmbientTransactionsDoNotOwnCacheWritesAsync(store)
            .ConfigureAwait(false);

        await AssertExternalDataSourceOwnershipAsync(store)
            .ConfigureAwait(false);

        await AssertWaitedRefreshDoesNotResurrectAsync(store)
            .ConfigureAwait(false);

        await AssertSlidingLockWaitCompletionAndCancellationAsync(store)
            .ConfigureAwait(false);

        await AssertCancellationAsync(store)
            .ConfigureAwait(false);

        await AssertBoundedCleanupAsync(store)
            .ConfigureAwait(false);

        await AssertDmlOnlyPermissionsAndCleanupFailureAsync(store)
            .ConfigureAwait(false);
    }

    private static async Task AssertExplicitIdempotentDeploymentAsync(
        CacheIntegrationStore store
    )
    {
        await Assert
            .ThrowsAsync<MySqlException>(() => store.Cache.GetAsync("before-deployment", CancellationToken.None))
            .ConfigureAwait(false);

        Assert.Equal(
            0L,
            await store
                .CountTablesAsync()
                .ConfigureAwait(false));

        var script = MySqlCacheSchema.GetCreateScript(store.SchemaName, store.TableName);

        await store
            .ExecuteAsync(script)
            .ConfigureAwait(false);

        await store
            .Cache
            .SetAsync("deployment-marker", [1], new DistributedCacheEntryOptions(), CancellationToken.None)
            .ConfigureAwait(false);

        await store
            .ExecuteAsync(script)
            .ConfigureAwait(false);

        Assert.Equal(
            new byte[] { 1 },
            await store
                .Cache
                .GetAsync("deployment-marker", CancellationToken.None)
                .ConfigureAwait(false));

        Assert.Equal(
            1L,
            await store
                .CountTablesAsync()
                .ConfigureAwait(false));

        Assert.Equal(
            $"Doka.Caching.MySql schema version {MySqlCacheSchema.Version}",
            await store
                .ReadTableCommentAsync()
                .ConfigureAwait(false));
    }

    // WHY: Exercise the synchronous byte-array and buffer contracts alongside their asynchronous counterparts.
    // ReSharper disable MethodHasAsyncOverload
    private static async Task AssertByteAndBufferContractsAsync(
        CacheIntegrationStore store
    )
    {
        var cache = store.Cache;
        var missingWriter = new ExactBufferWriter(0);

        Assert.Null(cache.Get("missing"));

        Assert.Null(
            await cache
                .GetAsync("missing", CancellationToken.None)
                .ConfigureAwait(false));

        Assert.False(cache.TryGet("missing", missingWriter));

        Assert.False(
            await cache
                .TryGetAsync("missing", missingWriter, CancellationToken.None)
                .ConfigureAwait(false));

        Assert.Equal(0, missingWriter.BufferRequests);
        Assert.Equal(0, missingWriter.WrittenCount);

        cache.Refresh("missing");

        await cache
            .RefreshAsync("missing", CancellationToken.None)
            .ConfigureAwait(false);

        cache.Remove("missing");

        await cache
            .RemoveAsync("missing", CancellationToken.None)
            .ConfigureAwait(false);

        foreach (var size in new[] { 0, 23, 8192, 1024 * 1024 })
        {
            var value = new byte[size];

            for (var index = 0; index < value.Length; index++)
            {
                value[index] = (byte)(index % 251);
            }

            var key = $"value-{size}";

            cache.Set(key, value, new DistributedCacheEntryOptions());

            Assert.Equal(value, cache.Get(key));

            Assert.Equal(
                value,
                await cache
                    .GetAsync(key, CancellationToken.None)
                    .ConfigureAwait(false));

            var syncDestination = new ExactBufferWriter(size);

            Assert.True(cache.TryGet(key, syncDestination));
            Assert.Equal(value, syncDestination.WrittenBytes);

            var asyncDestination = new ExactBufferWriter(size);

            Assert.True(
                await cache
                    .TryGetAsync(key, asyncDestination, CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.Equal(value, asyncDestination.WrittenBytes);

            if (size == 0)
            {
                Assert.Equal(0, syncDestination.BufferRequests);
                Assert.Equal(0, asyncDestination.BufferRequests);
            }

            cache.Remove(key);

            Assert.Null(
                await cache
                    .GetAsync(key, CancellationToken.None)
                    .ConfigureAwait(false));

            await cache
                .SetAsync(key, value, new DistributedCacheEntryOptions(), CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Equal(value, cache.Get(key));

            await cache
                .RemoveAsync(key, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Null(cache.Get(key));

            var sequence = new ReadOnlySequence<byte>(value);

            cache.Set(key, sequence, new DistributedCacheEntryOptions());

            Assert.Equal(
                value,
                await cache
                    .GetAsync(key, CancellationToken.None)
                    .ConfigureAwait(false));

            await cache
                .SetAsync(key, sequence, new DistributedCacheEntryOptions(), CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Equal(value, cache.Get(key));

            if (size > 1)
            {
                var first = new CacheSequenceSegment(value.AsMemory(0, size / 2));
                var last = first.Append(value.AsMemory(size / 2));
                var segmented = new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);

                cache.Set(key, segmented, new DistributedCacheEntryOptions());

                Assert.Equal(
                    value,
                    await cache
                        .GetAsync(key, CancellationToken.None)
                        .ConfigureAwait(false));

                await cache
                    .SetAsync(key, segmented, new DistributedCacheEntryOptions(), CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.Equal(value, cache.Get(key));
            }
        }
    }
    // ReSharper restore MethodHasAsyncOverload

    private static async Task AssertSlidingLockWaitCompletionAndCancellationAsync(
        CacheIntegrationStore store
    )
    {
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

        Func<CancellationToken, Task>[] reads =
        [
            async token => Assert.Equal(
                new byte[] { 1 },
                await cache
                    .GetAsync(key, token)
                    .ConfigureAwait(false)),
            async token => Assert.True(
                await cache
                    .TryGetAsync(key, new ExactBufferWriter(1), token)
                    .ConfigureAwait(false)),
        ];

        foreach (var read in reads)
        {
            foreach (var cancel in new[] { false, true })
            {
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
                var pending = read(cancellation.Token);
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

                        await Assert
                            .ThrowsAnyAsync<OperationCanceledException>(() =>
                                pending.WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None))
                            .ConfigureAwait(false);

                        Assert.Equal(
                            before,
                            await store
                                .ReadEntryAsync(key)
                                .ConfigureAwait(false));
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

                        var after = await store
                            .ReadEntryAsync(key)
                            .ConfigureAwait(false);

                        Assert.Equal(before.Revision, after.Revision);
                        Assert.True(after.ExpiresAt > before.ExpiresAt);
                    }

                    Assert.Null(
                        await cache
                            .GetAsync("single-pool-recovered-miss", CancellationToken.None)
                            .WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None)
                            .ConfigureAwait(false));
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
                        await Assert
                            .ThrowsAnyAsync<OperationCanceledException>(() =>
                                pending.WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None))
                            .ConfigureAwait(false);
                    }
                }
            }
        }
    }

    private static async Task AssertQuotedTableIdentifierAsync(
        CacheIntegrationStore store
    )
    {
        var tableName = store.TableName + "`quoted";
        var qualifiedName = MySqlCacheIdentifier.GetQualifiedName(store.SchemaName, tableName);

        await store
            .ExecuteAsync(MySqlCacheSchema.GetCreateScript(store.SchemaName, tableName))
            .ConfigureAwait(false);

        try
        {
            var services = new ServiceCollection();
            services.AddDistributedMySqlCache(options =>
            {
                options.ConnectionString = store.ConnectionString;
                options.SchemaName = store.SchemaName;
                options.TableName = tableName;
            });

            await using var provider = services.BuildServiceProvider();
            var cache = provider.GetRequiredService<IDistributedCache>();

            await cache
                .SetAsync("quoted", [1, 2], new DistributedCacheEntryOptions(), CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Equal(
                new byte[] { 1, 2 },
                await cache
                    .GetAsync("quoted", CancellationToken.None)
                    .ConfigureAwait(false));

            await cache
                .RemoveAsync("quoted", CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Null(
                await cache
                    .GetAsync("quoted", CancellationToken.None)
                    .ConfigureAwait(false));
        }
        finally
        {
            await store
                .ExecuteAsync($"DROP TABLE {qualifiedName};")
                .ConfigureAwait(false);
        }
    }

    private static async Task AssertDeploymentTableIsolationAsync(
        CacheIntegrationStore previousStore,
        IntegrationDatabaseTarget target
    )
    {
        const string key = "deployment-isolation";
        var options = new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(10),
        };

        await using (var nextStore = await CacheIntegrationStore
                         .CreateAsync(target)
                         .ConfigureAwait(false))
        {
            Assert.Equal(previousStore.SchemaName, nextStore.SchemaName);
            Assert.NotEqual(previousStore.TableName, nextStore.TableName);

            await nextStore
                .ExecuteAsync(MySqlCacheSchema.GetCreateScript(nextStore.SchemaName, nextStore.TableName))
                .ConfigureAwait(false);

            await previousStore
                .Cache
                .SetAsync(key, [1], options, CancellationToken.None)
                .ConfigureAwait(false);

            await nextStore
                .Cache
                .SetAsync(key, [2], options, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Equal(
                new byte[] { 1 },
                await previousStore
                    .Cache
                    .GetAsync(key, CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.Equal(
                new byte[] { 2 },
                await nextStore
                    .Cache
                    .GetAsync(key, CancellationToken.None)
                    .ConfigureAwait(false));

            foreach (var store in new[] { previousStore, nextStore })
            {
                await store
                    .ExecuteForKeyAsync(
                        $"UPDATE {store.QualifiedTableName} "
                        + "SET `ExpiresAtUtc` = TIMESTAMPADD(MINUTE, 1, UTC_TIMESTAMP(6)) "
                        + "WHERE `Id` = CAST(@key AS BINARY);",
                        key)
                    .ConfigureAwait(false);
            }

            var previousState = await previousStore
                .ReadEntryAsync(key)
                .ConfigureAwait(false);

            var nextState = await nextStore
                .ReadEntryAsync(key)
                .ConfigureAwait(false);

            await nextStore
                .Cache
                .RefreshAsync(key, CancellationToken.None)
                .ConfigureAwait(false);

            var nextRefreshedState = await nextStore
                .ReadEntryAsync(key)
                .ConfigureAwait(false);

            Assert.True(nextRefreshedState.ExpiresAt > nextState.ExpiresAt);

            Assert.Equal(
                previousState,
                await previousStore
                    .ReadEntryAsync(key)
                    .ConfigureAwait(false));

            await previousStore
                .Cache
                .RefreshAsync(key, CancellationToken.None)
                .ConfigureAwait(false);

            var previousRefreshedState = await previousStore
                .ReadEntryAsync(key)
                .ConfigureAwait(false);

            Assert.True(previousRefreshedState.ExpiresAt > previousState.ExpiresAt);

            Assert.Equal(
                nextRefreshedState,
                await nextStore
                    .ReadEntryAsync(key)
                    .ConfigureAwait(false));

            await previousStore
                .Cache
                .SetAsync(key, [3], options, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Equal(
                new byte[] { 2 },
                await nextStore
                    .Cache
                    .GetAsync(key, CancellationToken.None)
                    .ConfigureAwait(false));

            await nextStore
                .Cache
                .SetAsync(key, [4], options, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Equal(
                new byte[] { 3 },
                await previousStore
                    .Cache
                    .GetAsync(key, CancellationToken.None)
                    .ConfigureAwait(false));

            await previousStore
                .Cache
                .RemoveAsync(key, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Null(
                await previousStore
                    .Cache
                    .GetAsync(key, CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.Equal(
                new byte[] { 4 },
                await nextStore
                    .Cache
                    .GetAsync(key, CancellationToken.None)
                    .ConfigureAwait(false));

            await previousStore
                .Cache
                .SetAsync(key, [3], options, CancellationToken.None)
                .ConfigureAwait(false);

            await nextStore
                .Cache
                .RemoveAsync(key, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Null(
                await nextStore
                    .Cache
                    .GetAsync(key, CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.Equal(
                new byte[] { 3 },
                await previousStore
                    .Cache
                    .GetAsync(key, CancellationToken.None)
                    .ConfigureAwait(false));

            await nextStore
                .Cache
                .SetAsync(key, [4], options, CancellationToken.None)
                .ConfigureAwait(false);
        }

        Assert.Equal(
            new byte[] { 3 },
            await previousStore
                .Cache
                .GetAsync(key, CancellationToken.None)
                .ConfigureAwait(false));

        await previousStore
            .Cache
            .SetAsync(key, [5], options, CancellationToken.None)
            .ConfigureAwait(false);

        Assert.Equal(
            new byte[] { 5 },
            await previousStore
                .Cache
                .GetAsync(key, CancellationToken.None)
                .ConfigureAwait(false));
    }

    private static async Task AssertOrdinalKeysAsync(
        CacheIntegrationStore store
    )
    {
        string[] keys =
        [
            "case",
            "CASE",
            "space",
            "space ",
            "nul",
            "nul\0",
            "\u00e9",
            "e\u0301",
            "\ud83d\ude80",
            "'; DROP TABLE cache; --",
            new string('a', 1024),
            new string('\u00e9', 512),
        ];

        for (var index = 0; index < keys.Length; index++)
        {
            await store
                .Cache
                .SetAsync(keys[index], [(byte)index], new DistributedCacheEntryOptions(), CancellationToken.None)
                .ConfigureAwait(false);
        }

        for (var index = 0; index < keys.Length; index++)
        {
            Assert.Equal(
                new[] { (byte)index },
                await store
                    .Cache
                    .GetAsync(keys[index], CancellationToken.None)
                    .ConfigureAwait(false));
        }

        await Assert
            .ThrowsAsync<ArgumentException>(() => store.Cache.SetAsync(
                new string('\u00e9', 513),
                [1],
                new DistributedCacheEntryOptions(),
                CancellationToken.None))
            .ConfigureAwait(false);

        // WHY: Keep coverage of malformed-key validation through the synchronous Get API.
        // ReSharper disable once MethodHasAsyncOverload
        Assert.Throws<ArgumentException>(() => store.Cache.Get("invalid\ud800"));

        Assert.Equal(
            new byte[] { 0 },
            await store
                .Cache
                .GetAsync("case", CancellationToken.None)
                .ConfigureAwait(false));
    }

    // WHY: Expiration and refresh must be checked through both synchronous and asynchronous cache APIs.
    // ReSharper disable MethodHasAsyncOverload
    private static async Task AssertExpirationContractsAsync(
        CacheIntegrationStore store
    )
    {
        var cache = store.Cache;

        var before = await store
            .ReadUtcNowAsync()
            .ConfigureAwait(false);

        cache.Set("default-expiration", [1], new DistributedCacheEntryOptions());

        var after = await store
            .ReadUtcNowAsync()
            .ConfigureAwait(false);

        var defaults = await store
            .ReadEntryAsync("default-expiration")
            .ConfigureAwait(false);

        Assert.Equal(20 * 60 * 1_000_000L, defaults.SlidingMicroseconds);
        Assert.Null(defaults.AbsoluteExpiration);
        Assert.InRange(defaults.ExpiresAt, before.AddMinutes(20), after.AddMinutes(20));

        var absolute = new DateTimeOffset(DateTime.SpecifyKind(after.AddHours(1), DateTimeKind.Utc))
            .ToOffset(TimeSpan.FromHours(7));

        await cache
            .SetAsync(
                "absolute",
                [2],
                new DistributedCacheEntryOptions { AbsoluteExpiration = absolute },
                CancellationToken.None)
            .ConfigureAwait(false);

        var absoluteState = await store
            .ReadEntryAsync("absolute")
            .ConfigureAwait(false);

        Assert.Equal(absolute.UtcDateTime, absoluteState.ExpiresAt);
        Assert.Equal(absolute.UtcDateTime, absoluteState.AbsoluteExpiration);
        Assert.Null(absoluteState.SlidingMicroseconds);

        cache.Refresh("absolute");

        await cache
            .RefreshAsync("absolute", CancellationToken.None)
            .ConfigureAwait(false);

        Assert.Equal(
            new byte[] { 2 },
            await cache
                .GetAsync("absolute", CancellationToken.None)
                .ConfigureAwait(false));

        Assert.Equal(
            absoluteState,
            await store
                .ReadEntryAsync("absolute")
                .ConfigureAwait(false));

        before = await store
            .ReadUtcNowAsync()
            .ConfigureAwait(false);

        await cache
            .SetAsync(
                "relative",
                [3],
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30),
                },
                CancellationToken.None)
            .ConfigureAwait(false);

        after = await store
            .ReadUtcNowAsync()
            .ConfigureAwait(false);

        var relative = await store
            .ReadEntryAsync("relative")
            .ConfigureAwait(false);

        Assert.InRange(relative.ExpiresAt, before.AddMinutes(30), after.AddMinutes(30));
        Assert.Equal(relative.ExpiresAt, relative.AbsoluteExpiration);
        Assert.Null(relative.SlidingMicroseconds);

        await cache
            .SetAsync(
                "sliding",
                [4],
                new DistributedCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromMinutes(10),
                },
                CancellationToken.None)
            .ConfigureAwait(false);

        await store
            .ExecuteForKeyAsync(
                $"UPDATE {store.QualifiedTableName} SET `ExpiresAtUtc` = TIMESTAMPADD(MINUTE, 1, UTC_TIMESTAMP(6)) "
                + "WHERE `Id` = CAST(@key AS BINARY);",
                "sliding")
            .ConfigureAwait(false);

        before = await store
            .ReadUtcNowAsync()
            .ConfigureAwait(false);

        Assert.Equal(new byte[] { 4 }, cache.Get("sliding"));

        after = await store
            .ReadUtcNowAsync()
            .ConfigureAwait(false);

        var refreshed = await store
            .ReadEntryAsync("sliding")
            .ConfigureAwait(false);

        Assert.InRange(refreshed.ExpiresAt, before.AddMinutes(10), after.AddMinutes(10));

        await cache
            .SetAsync(
                "capped",
                [5],
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2),
                    SlidingExpiration = TimeSpan.FromMinutes(10),
                },
                CancellationToken.None)
            .ConfigureAwait(false);

        var capped = await store
            .ReadEntryAsync("capped")
            .ConfigureAwait(false);

        Assert.Equal(capped.AbsoluteExpiration, capped.ExpiresAt);

        await cache
            .RefreshAsync("capped", CancellationToken.None)
            .ConfigureAwait(false);

        var buffer = new ExactBufferWriter(1);

        Assert.True(
            await cache
                .TryGetAsync("capped", buffer, CancellationToken.None)
                .ConfigureAwait(false));

        Assert.Equal(
            capped,
            await store
                .ReadEntryAsync("capped")
                .ConfigureAwait(false));

        await store
            .ExecuteForKeyAsync(
                $"UPDATE {store.QualifiedTableName} SET `ExpiresAtUtc` = TIMESTAMPADD(SECOND, -1, UTC_TIMESTAMP(6)) "
                + "WHERE `Id` = CAST(@key AS BINARY);",
                "sliding")
            .ConfigureAwait(false);

        var expired = await store
            .ReadEntryAsync("sliding")
            .ConfigureAwait(false);

        var expiredDestination = new ExactBufferWriter(0);

        Assert.Null(cache.Get("sliding"));

        Assert.Null(
            await cache
                .GetAsync("sliding", CancellationToken.None)
                .ConfigureAwait(false));

        Assert.False(cache.TryGet("sliding", expiredDestination));

        Assert.False(
            await cache
                .TryGetAsync("sliding", expiredDestination, CancellationToken.None)
                .ConfigureAwait(false));

        cache.Refresh("sliding");

        await cache
            .RefreshAsync("sliding", CancellationToken.None)
            .ConfigureAwait(false);

        Assert.Equal(
            expired,
            await store
                .ReadEntryAsync("sliding")
                .ConfigureAwait(false));

        Assert.Equal(0, expiredDestination.BufferRequests);

        await cache
            .SetAsync("sliding", [6], new DistributedCacheEntryOptions(), CancellationToken.None)
            .ConfigureAwait(false);

        Assert.Equal(new byte[] { 6 }, cache.Get("sliding"));

        await cache
            .SetAsync(
                "already-expired",
                [7],
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpiration =
                        new DateTimeOffset(DateTime.SpecifyKind(before.AddHours(-1), DateTimeKind.Utc)),
                },
                CancellationToken.None)
            .ConfigureAwait(false);

        Assert.Null(
            await cache
                .GetAsync("already-expired", CancellationToken.None)
                .ConfigureAwait(false));
    }
    // ReSharper restore MethodHasAsyncOverload

    private static async Task AssertConcurrentUpsertsAsync(
        CacheIntegrationStore store
    )
    {
        var concurrentConnectionString = new MySqlConnectionStringBuilder(store.ConnectionString)
        {
            // WHY: The upsert race must not depend on the shared fixture's connection-pool limit.
            MaximumPoolSize = ConcurrentUpsertProbeCount + 1,
        }.ConnectionString;
        await using var cache = store.CreateCache(TimeProvider.System, connectionString: concurrentConnectionString);
        var writes = Enumerable
            .Range(1, ConcurrentUpsertProbeCount)
            .Select(async marker =>
            {
                var value = new byte[64 * 1024];

                value
                    .AsSpan()
                    .Fill((byte)marker);

                await cache
                    .SetAsync("concurrent", value, new DistributedCacheEntryOptions(), CancellationToken.None)
                    .ConfigureAwait(false);

                var observed = await cache
                    .GetAsync("concurrent", CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.NotNull(observed);
                Assert.Equal(value.Length, observed.Length);
                Assert.InRange(observed[0], (byte)1, (byte)16);

                Assert.True(
                    observed
                        .AsSpan()
                        .IndexOfAnyExcept(observed[0])
                    < 0);
            });

        await Task
            .WhenAll(writes)
            .ConfigureAwait(false);

        Assert.Equal(
            1L,
            await store
                .CountKeyAsync("concurrent")
                .ConfigureAwait(false));

        for (var iteration = 0; iteration < 8; iteration++)
        {
            await cache
                .SetAsync("remove-race", [1], new DistributedCacheEntryOptions(), CancellationToken.None)
                .ConfigureAwait(false);

            await Task
                .WhenAll(
                    cache.RefreshAsync("remove-race", CancellationToken.None),
                    cache.RemoveAsync("remove-race", CancellationToken.None))
                .ConfigureAwait(false);

            Assert.Null(
                await cache
                    .GetAsync("remove-race", CancellationToken.None)
                    .ConfigureAwait(false));
        }
    }

    // WHY: The useAsync=false branch deliberately tests synchronous TryGet during replacement and removal races.
    // ReSharper disable MethodHasAsyncOverload
    private static async Task AssertReadRefreshRacesAsync(
        CacheIntegrationStore store,
        bool useAsync
    )
    {
        var key = $"read-refresh-race-{useAsync}";

        await store
            .Cache
            .SetAsync(key, [1, 2], new DistributedCacheEntryOptions(), CancellationToken.None)
            .ConfigureAwait(false);

        using (var paused = new PausingBufferWriter(2))
        {
            var reading = Task.Run(
                async () => useAsync
                    ? await store
                        .Cache
                        .TryGetAsync(key, paused, CancellationToken.None)
                        .ConfigureAwait(false)
                    : store.Cache.TryGet(key, paused),
                CancellationToken.None);

            CacheIntegrationStore.EntryState replacement;

            try
            {
                await paused
                    .ReadStarted
                    .WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None)
                    .ConfigureAwait(false);

                await store
                    .Cache
                    .SetAsync(key, [3, 4], new DistributedCacheEntryOptions(), CancellationToken.None)
                    .ConfigureAwait(false);

                await store
                    .ExecuteForKeyAsync(
                        $"UPDATE {store.QualifiedTableName} "
                        + "SET `ExpiresAtUtc` = TIMESTAMPADD(MINUTE, 1, UTC_TIMESTAMP(6)) "
                        + "WHERE `Id` = CAST(@key AS BINARY);",
                        key)
                    .ConfigureAwait(false);

                replacement = await store
                    .ReadEntryAsync(key)
                    .ConfigureAwait(false);
            }
            finally
            {
                paused.Release();
            }

            Assert.True(
                await reading
                    .WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.Equal(new byte[] { 1, 2 }, paused.WrittenBytes);

            Assert.Equal(
                replacement,
                await store
                    .ReadEntryAsync(key)
                    .ConfigureAwait(false));
        }

        using (var paused = new PausingBufferWriter(2))
        {
            var reading = Task.Run(
                async () => useAsync
                    ? await store
                        .Cache
                        .TryGetAsync(key, paused, CancellationToken.None)
                        .ConfigureAwait(false)
                    : store.Cache.TryGet(key, paused),
                CancellationToken.None);

            try
            {
                await paused
                    .ReadStarted
                    .WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None)
                    .ConfigureAwait(false);

                await store
                    .Cache
                    .RemoveAsync(key, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            finally
            {
                paused.Release();
            }

            Assert.True(
                await reading
                    .WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.Equal(new byte[] { 3, 4 }, paused.WrittenBytes);

            Assert.Null(
                await store
                    .Cache
                    .GetAsync(key, CancellationToken.None)
                    .ConfigureAwait(false));
        }
    }
    // ReSharper restore MethodHasAsyncOverload

    private static async Task AssertCancellationAsync(
        CacheIntegrationStore store
    )
    {
        var connectionString = new MySqlConnectionStringBuilder(store.ConnectionString)
        {
            MaximumPoolSize = 1,
            ConnectionTimeout = 3,
        }.ConnectionString;

        await using var cache = store.CreateCache(new CacheManualTimeProvider(), connectionString: connectionString);

        await cache
            .SetAsync("cancelled", [1], new DistributedCacheEntryOptions(), CancellationToken.None)
            .ConfigureAwait(false);

        var firstSegment = new CacheSequenceSegment(new byte[] { 3 });
        var lastSegment = firstSegment.Append(new byte[] { 4 });
        var segmented = new ReadOnlySequence<byte>(firstSegment, 0, lastSegment, lastSegment.Memory.Length);

        Func<CancellationToken, Task>[] operations =
        [
            token => cache.GetAsync("cancelled", token),
            token => cache.SetAsync("cancelled", new byte[] { 2 }, new DistributedCacheEntryOptions(), token),
            token => cache.RefreshAsync("cancelled", token),
            token => cache.RemoveAsync("cancelled", token),
            token => cache
                .TryGetAsync("cancelled", new ExactBufferWriter(1), token)
                .AsTask(),
            token => cache
                .SetAsync(
                    "cancelled",
                    new ReadOnlySequence<byte>(new byte[] { 3 }),
                    new DistributedCacheEntryOptions(),
                    token)
                .AsTask(),
            token => cache
                .SetAsync("cancelled", segmented, new DistributedCacheEntryOptions(), token)
                .AsTask(),
        ];

        await using var observer = new MySqlConnection(store.ConnectionString);

        await observer
            .OpenAsync(CancellationToken.None)
            .ConfigureAwait(false);

        foreach (var operation in operations)
        {
            using var cancellation = new CancellationTokenSource();
            Task? pending = null;

            await store
                .ExecuteAsync($"LOCK TABLES {store.QualifiedTableName} WRITE;")
                .ConfigureAwait(false);

            try
            {
                pending = operation(cancellation.Token);

                await AssertWaitingForTableLockAsync(observer, store.TableName, pending)
                    .ConfigureAwait(false);

                await cancellation
                    .CancelAsync()
                    .ConfigureAwait(false);

                await Assert
                    .ThrowsAnyAsync<OperationCanceledException>(
                        () => pending.WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None))
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
                        .ThrowsAnyAsync<OperationCanceledException>(
                            () => pending.WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None))
                        .ConfigureAwait(false);
                }
            }

            Assert.Equal(
                new byte[] { 1 },
                await cache
                    .GetAsync("cancelled", CancellationToken.None)
                    .ConfigureAwait(false));
        }
    }

    // WHY: The row-lock regression must cover synchronous and asynchronous Get, TryGet, and Refresh.
    // ReSharper disable MethodHasAsyncOverload
    private static async Task AssertWaitedRefreshDoesNotResurrectAsync(
        CacheIntegrationStore store
    )
    {
        const string key = "expired-during-lock-wait";

        Func<Task>[] operations =
        [
            () => Task.Run(() => store.Cache.Refresh(key), CancellationToken.None),
            () => store.Cache.RefreshAsync(key, CancellationToken.None),
            () => Task.Run(() => store.Cache.Get(key), CancellationToken.None),
            () => store.Cache.GetAsync(key, CancellationToken.None),
            () => Task.Run(() => store.Cache.TryGet(key, new ExactBufferWriter(1)), CancellationToken.None),
            () => store
                .Cache
                .TryGetAsync(key, new ExactBufferWriter(1), CancellationToken.None)
                .AsTask(),
        ];

        await using var lockConnection = new MySqlConnection(store.ConnectionString);

        await lockConnection
            .OpenAsync(CancellationToken.None)
            .ConfigureAwait(false);

        await using var observer = new MySqlConnection(store.ConnectionString);

        await observer
            .OpenAsync(CancellationToken.None)
            .ConfigureAwait(false);

        foreach (var operation in operations)
        {
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

            var pending = operation();

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

            Assert.True(
                entry.ExpiresAt <= now,
                "A refresh that waited for a row lock extended a row that expired before the lock was released.");

            Assert.Null(
                await store
                    .Cache
                    .GetAsync(key, CancellationToken.None)
                    .ConfigureAwait(false));
        }
    }
    // ReSharper restore MethodHasAsyncOverload

    private static async Task AssertLockingStatementStartedAsync(
        MySqlConnection observer,
        string tableName,
        Task pending,
        bool cleanupOnly = false
    )
    {
        await using var command = observer.CreateCommand();

        // Unique-key locking reads can wait in optimization before INNODB_TRX exposes the wait.
        command.CommandText = """
                              SELECT COUNT(*) FROM information_schema.PROCESSLIST
                              WHERE ID <> CONNECTION_ID()
                                  AND LOCATE(@tableName, COALESCE(INFO, '')) > 0
                                  AND (UPPER(INFO) LIKE '%FOR UPDATE%'
                                      OR UPPER(LTRIM(INFO)) LIKE 'UPDATE %'
                                      OR UPPER(LTRIM(INFO)) LIKE 'DELETE %')
                                  AND (@cleanupOnly = FALSE OR UPPER(LTRIM(INFO)) LIKE 'DELETE %');
                              """;

        command.Parameters.AddWithValue("@tableName", tableName);
        command.Parameters.AddWithValue("@cleanupOnly", cleanupOnly);

        var timeout = Stopwatch.StartNew();

        while (timeout.Elapsed < TimeSpan.FromSeconds(15))
        {
            Assert.False(pending.IsCompleted, "The cache operation completed before it reached the row lock.");

            var waiting = Convert.ToInt64(
                await command
                    .ExecuteScalarAsync(CancellationToken.None)
                    .ConfigureAwait(false),
                CultureInfo.InvariantCulture);

            if (waiting > 0)
            {
                return;
            }

            await Task
                .Delay(10, CancellationToken.None)
                .ConfigureAwait(false);
        }

        Assert.Fail("The cache operation did not reach the database row lock within the test deadline.");
    }

    private static async Task AssertAmbientTransactionsDoNotOwnCacheWritesAsync(
        CacheIntegrationStore store
    )
    {
        using (var scope = new System.Transactions.TransactionScope(
                   System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
        {
            // WHY: The synchronous write must survive ambient rollback independently of SetAsync.
            // ReSharper disable once MethodHasAsyncOverload
            store.Cache.Set("ambient-sync", [1], new DistributedCacheEntryOptions());

            await store
                .Cache
                .SetAsync("ambient-async", [2], new DistributedCacheEntryOptions(), CancellationToken.None)
                .ConfigureAwait(false);
        }

        Assert.Equal(
            new byte[] { 1 },
            await store
                .Cache
                .GetAsync("ambient-sync", CancellationToken.None)
                .ConfigureAwait(false));

        Assert.Equal(
            new byte[] { 2 },
            await store
                .Cache
                .GetAsync("ambient-async", CancellationToken.None)
                .ConfigureAwait(false));
    }

    private static async Task AssertWaitingForTableLockAsync(
        MySqlConnection observer,
        string tableName,
        Task pending
    )
    {
        await using var command = observer.CreateCommand();
        command.CommandText = """
                              SELECT COUNT(*) FROM information_schema.PROCESSLIST
                              WHERE ID <> CONNECTION_ID()
                                  AND LOCATE(@tableName, COALESCE(INFO, '')) > 0
                                  AND LOWER(COALESCE(STATE, '')) LIKE '%lock%';
                              """;

        command.Parameters.AddWithValue("@tableName", tableName);

        var timeout = Stopwatch.StartNew();

        while (timeout.Elapsed < TimeSpan.FromSeconds(15))
        {
            Assert.False(pending.IsCompleted, "The cache operation completed before it reached the table lock.");

            var waiting = Convert.ToInt64(
                await command
                    .ExecuteScalarAsync(CancellationToken.None)
                    .ConfigureAwait(false),
                CultureInfo.InvariantCulture);

            if (waiting > 0)
            {
                return;
            }

            await Task
                .Delay(10, CancellationToken.None)
                .ConfigureAwait(false);
        }

        Assert.Fail("The cache operation did not reach the database table lock within the test deadline.");
    }

    private static async Task AssertExternalDataSourceOwnershipAsync(
        CacheIntegrationStore store
    )
    {
        var connectionBuilder = new MySqlConnectionStringBuilder(store.ConnectionString)
        {
            AutoEnlist = false,
            MaximumPoolSize = 1,
        };

        var password = connectionBuilder.Password;
        connectionBuilder.Remove("Password");
        var passwordRequests = 0;
        var connectionOpens = 0;
        var dataSourceBuilder = new MySqlDataSourceBuilder(connectionBuilder.ConnectionString);

        dataSourceBuilder.UsePeriodicPasswordProvider(
            (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                Interlocked.Increment(ref passwordRequests);
                return ValueTask.FromResult(password);
            },
            TimeSpan.FromHours(1),
            TimeSpan.FromSeconds(1));

        dataSourceBuilder.UseConnectionOpenedCallback((_, token) =>
        {
            token.ThrowIfCancellationRequested();
            Interlocked.Increment(ref connectionOpens);
            return ValueTask.CompletedTask;
        });

        await using var dataSource = dataSourceBuilder.Build();
        var services = new ServiceCollection();
        services.AddDistributedMySqlCache(options =>
        {
            options.DataSource = dataSource;
            options.SchemaName = store.SchemaName;
            options.TableName = store.TableName;
        });

        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<IBufferDistributedCache>();

        using (var scope = new System.Transactions.TransactionScope(
                   System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
        {
            await cache
                .SetAsync("external-source", [4, 2], new DistributedCacheEntryOptions(), CancellationToken.None)
                .ConfigureAwait(false);
        }

        Assert.Equal(
            new byte[] { 4, 2 },
            await cache
                .GetAsync("external-source", CancellationToken.None)
                .ConfigureAwait(false));

        Assert.True(Volatile.Read(ref passwordRequests) > 0);
        Assert.True(Volatile.Read(ref connectionOpens) > 0);

        await using (var lease = await dataSource
                         .OpenConnectionAsync(CancellationToken.None)
                         .ConfigureAwait(false))
        {
            using var cancellation = new CancellationTokenSource();
            var pending = cache.GetAsync("external-source", cancellation.Token);

            try
            {
                Assert.False(
                    pending.IsCompleted,
                    "The cache must share the caller's exhausted single-connection pool.");
            }
            finally
            {
                await cancellation
                    .CancelAsync()
                    .ConfigureAwait(false);

                await Assert
                    .ThrowsAnyAsync<OperationCanceledException>(() =>
                        pending.WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None))
                    .ConfigureAwait(false);
            }
        }

        Assert.Equal(
            new byte[] { 4, 2 },
            await cache
                .GetAsync("external-source", CancellationToken.None)
                .ConfigureAwait(false));

        await provider
            .DisposeAsync()
            .ConfigureAwait(false);

        await Assert
            .ThrowsAsync<ObjectDisposedException>(() => cache.GetAsync("external-source", CancellationToken.None))
            .ConfigureAwait(false);

        await using var survivingConnection = await dataSource
            .OpenConnectionAsync(CancellationToken.None)
            .ConfigureAwait(false);

        await using var command = new MySqlCommand("SELECT 1;", survivingConnection);

        Assert.Equal(
            1L,
            Convert.ToInt64(
                await command
                    .ExecuteScalarAsync(CancellationToken.None)
                    .ConfigureAwait(false),
                CultureInfo.InvariantCulture));
    }

    private static async Task AssertBoundedCleanupAsync(
        CacheIntegrationStore store
    )
    {
        await store
            .ExecuteAsync($"DELETE FROM {store.QualifiedTableName};")
            .ConfigureAwait(false);

        await store
            .InsertExpiredEntriesAsync(1005)
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

        Assert.Equal(
            1005L,
            await store
                .CountExpiredAsync()
                .ConfigureAwait(false));

        // WHY: A synchronous miss must leave expired rows untouched before the cleanup interval is due.
        // ReSharper disable once MethodHasAsyncOverload
        Assert.Null(cache.Get("cleanup-miss"));

        Assert.Equal(
            1005L,
            await store
                .CountExpiredAsync()
                .ConfigureAwait(false));

        time.Advance(TimeSpan.FromMinutes(5));

        // WHY: Verify the synchronous 1,000-row cleanup bound before exercising the asynchronous path.
        // ReSharper disable once MethodHasAsyncOverload
        Assert.Null(cache.Get("cleanup-miss"));

        Assert.Equal(
            5L,
            await store
                .CountExpiredAsync()
                .ConfigureAwait(false));

        Assert.Null(
            await cache
                .GetAsync("cleanup-miss", CancellationToken.None)
                .ConfigureAwait(false));

        Assert.Equal(
            0L,
            await store
                .CountExpiredAsync()
                .ConfigureAwait(false));

        await store
            .InsertExpiredEntriesAsync(2)
            .ConfigureAwait(false);

        Assert.Null(
            await cache
                .GetAsync("cleanup-miss", CancellationToken.None)
                .ConfigureAwait(false));

        Assert.Equal(
            2L,
            await store
                .CountExpiredAsync()
                .ConfigureAwait(false));

        time.Advance(TimeSpan.FromMinutes(5));

        Assert.Null(
            await cache
                .GetAsync("cleanup-miss", CancellationToken.None)
                .ConfigureAwait(false));

        Assert.Equal(
            0L,
            await store
                .CountExpiredAsync()
                .ConfigureAwait(false));

        Assert.Equal(
            new byte[] { 1 },
            await cache
                .GetAsync("live", CancellationToken.None)
                .ConfigureAwait(false));

        await store
            .InsertExpiredEntriesAsync(2005)
            .ConfigureAwait(false);

        await AssertCleanupCancellationAsync(store, cache, time)
            .ConfigureAwait(false);

        Assert.Empty(logger.Entries);

        time.Advance(TimeSpan.FromMinutes(5));

        // WHY: The synchronous path must also keep a full batch immediately eligible for a following caller.
        // ReSharper disable once MethodHasAsyncOverload
        Assert.Null(cache.Get("cleanup-miss"));

        Assert.Equal(
            1005L,
            await store
                .CountExpiredAsync()
                .ConfigureAwait(false));

        Assert.Equal(
            new byte[] { 1 },
            await cache
                .GetAsync("live", CancellationToken.None)
                .ConfigureAwait(false));

        Assert.Equal(
            5L,
            await store
                .CountExpiredAsync()
                .ConfigureAwait(false));

        Assert.Null(
            await cache
                .GetAsync("cleanup-miss", CancellationToken.None)
                .ConfigureAwait(false));

        Assert.Equal(
            0L,
            await store
                .CountExpiredAsync()
                .ConfigureAwait(false));
    }

    private static async Task AssertCleanupCancellationAsync(
        CacheIntegrationStore store,
        MySqlDistributedCache cache,
        CacheManualTimeProvider time
    )
    {
        await store
            .ExecuteForKeyAsync(
                $"UPDATE {store.QualifiedTableName} SET `ExpiresAtUtc` = TIMESTAMPADD(SECOND, -2, UTC_TIMESTAMP(6)) "
                + "WHERE `Id` = CAST(@key AS BINARY);",
                "expired-0")
            .ConfigureAwait(false);

        await using var lockConnection = new MySqlConnection(store.ConnectionString);

        await lockConnection
            .OpenAsync(CancellationToken.None)
            .ConfigureAwait(false);

        await using var observer = new MySqlConnection(store.ConnectionString);

        await observer
            .OpenAsync(CancellationToken.None)
            .ConfigureAwait(false);

        var first = new CacheSequenceSegment(new byte[] { 3 });
        var last = first.Append(new byte[] { 4 });
        var segmented = new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);

        Func<CancellationToken, Task>[] operations =
        [
            async token => Assert.Equal(
                new byte[] { 1 },
                await cache
                    .GetAsync("live", token)
                    .ConfigureAwait(false)),
            async token =>
            {
                var destination = new ExactBufferWriter(1);
                Assert.True(
                    await cache
                        .TryGetAsync("live", destination, token)
                        .ConfigureAwait(false));

                Assert.Equal(new byte[] { 1 }, destination.WrittenBytes);
            },
            async token =>
            {
                await cache
                    .SetAsync("cleanup-array", [2], new DistributedCacheEntryOptions(), token)
                    .ConfigureAwait(false);

                Assert.Equal(
                    new byte[] { 2 },
                    await cache
                        .GetAsync("cleanup-array", CancellationToken.None)
                        .ConfigureAwait(false));
            },
            token => cache
                .SetAsync(
                    "cleanup-sequence",
                    new ReadOnlySequence<byte>(new byte[] { 3 }),
                    new DistributedCacheEntryOptions(),
                    token)
                .AsTask(),
            token => cache
                .SetAsync("cleanup-segmented", segmented, new DistributedCacheEntryOptions(), token)
                .AsTask(),
            token => cache.RefreshAsync("live", token),
            token => cache.RemoveAsync("cleanup-array", token),
        ];

        for (var operationIndex = 0; operationIndex < operations.Length; operationIndex++)
        {
            var operation = operations[operationIndex];
            await using var transaction = await lockConnection
                .BeginTransactionAsync(CancellationToken.None)
                .ConfigureAwait(false);

            await using (var locked = new MySqlCommand(
                             $"SELECT `Revision` FROM {store.QualifiedTableName} "
                             + "WHERE `Id` = CAST('expired-0' AS BINARY) FOR UPDATE;",
                             lockConnection,
                             transaction))
            {
                await locked
                    .ExecuteScalarAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }

            using var cancellation = new CancellationTokenSource();
            time.Advance(TimeSpan.FromMinutes(5));
            var pending = operation(cancellation.Token);

            try
            {
                await AssertLockingStatementStartedAsync(observer, store.TableName, pending, cleanupOnly: true)
                    .ConfigureAwait(false);

                if (operationIndex == 0)
                {
                    // WHY: The isolated pool makes this a cleanup-coordination assertion, not a pool-capacity test.
                    Assert.Null(
                        await cache
                            .GetAsync("cleanup-miss", CancellationToken.None)
                            .WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None)
                            .ConfigureAwait(false));
                }

                Assert.False(pending.IsCompleted);

                // A slow canceled batch starts its retry interval when it finishes, not when it starts.
                time.Advance(TimeSpan.FromMinutes(5));

                await cancellation
                    .CancelAsync()
                    .ConfigureAwait(false);

                await pending
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

            Assert.Equal(
                new byte[] { 1 },
                await cache
                    .GetAsync("live", CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.Equal(
                2005L,
                await store
                    .CountExpiredAsync()
                    .ConfigureAwait(false));
        }

        Assert.Null(
            await cache
                .GetAsync("cleanup-array", CancellationToken.None)
                .ConfigureAwait(false));

        Assert.Equal(
            new byte[] { 3 },
            await cache
                .GetAsync("cleanup-sequence", CancellationToken.None)
                .ConfigureAwait(false));

        Assert.Equal(
            new byte[] { 3, 4 },
            await cache
                .GetAsync("cleanup-segmented", CancellationToken.None)
                .ConfigureAwait(false));
    }

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

    private static async Task AssertDmlOnlyPermissionsAndCleanupFailureAsync(
        CacheIntegrationStore store
    )
    {
        var userName = "cache_"
            + Guid
                .NewGuid()
                .ToString("N")[..20];

        var password = Guid
            .NewGuid()
            .ToString("N");

        await store
            .ExecuteAsync($"CREATE USER '{userName}'@'%' IDENTIFIED BY '{password}';")
            .ConfigureAwait(false);

        try
        {
            await store
                .ExecuteAsync(
                    $"GRANT SELECT, INSERT, UPDATE, DELETE ON {store.QualifiedTableName} TO '{userName}'@'%';")
                .ConfigureAwait(false);

            var connectionString = new MySqlConnectionStringBuilder(store.ConnectionString)
            {
                UserID = userName,
                Password = password,
                Pooling = false,
            }.ConnectionString;

            var time = new CacheManualTimeProvider();
            var logger = new CacheRecordingLogger();
            await using var cache = store.CreateCache(time, logger, connectionString);
            const string privateKey = "private-cache-key";
            var privateValue = Encoding.UTF8.GetBytes("private-cache-value");

            await cache
                .SetAsync(privateKey, privateValue, new DistributedCacheEntryOptions(), CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Equal(
                privateValue,
                await cache
                    .GetAsync(privateKey, CancellationToken.None)
                    .ConfigureAwait(false));

            await cache
                .RefreshAsync(privateKey, CancellationToken.None)
                .ConfigureAwait(false);

            var destination = new ExactBufferWriter(privateValue.Length);

            Assert.True(
                await cache
                    .TryGetAsync(privateKey, destination, CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.Equal(privateValue, destination.WrittenBytes);

            await cache
                .RemoveAsync(privateKey, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Null(
                await cache
                    .GetAsync(privateKey, CancellationToken.None)
                    .ConfigureAwait(false));

            await using (var restrictedConnection = new MySqlConnection(connectionString))
            {
                await restrictedConnection
                    .OpenAsync(CancellationToken.None)
                    .ConfigureAwait(false);

                await using var ddl = restrictedConnection.CreateCommand();
                ddl.CommandText = $"ALTER TABLE {store.QualifiedTableName} COMMENT='not-authorized';";

                await Assert
                    .ThrowsAsync<MySqlException>(() => ddl.ExecuteNonQueryAsync(CancellationToken.None))
                    .ConfigureAwait(false);
            }

            await cache
                .SetAsync(privateKey, privateValue, new DistributedCacheEntryOptions(), CancellationToken.None)
                .ConfigureAwait(false);

            await store
                .ExecuteAsync($"REVOKE DELETE ON {store.QualifiedTableName} FROM '{userName}'@'%';")
                .ConfigureAwait(false);

            time.Advance(TimeSpan.FromMinutes(5));

            Assert.Equal(
                privateValue,
                await cache
                    .GetAsync(privateKey, CancellationToken.None)
                    .ConfigureAwait(false));

            // An empty candidate read does not issue DELETE, even when that privilege is absent.
            Assert.Empty(logger.Entries);

            await store
                .InsertExpiredEntriesAsync(1)
                .ConfigureAwait(false);

            time.Advance(TimeSpan.FromMinutes(5));

            Assert.Equal(
                privateValue,
                await cache
                    .GetAsync(privateKey, CancellationToken.None)
                    .ConfigureAwait(false));

            var log = Assert.Single(logger.Entries);

            Assert.Equal(LogLevel.Warning, log.Level);
            Assert.Equal(1, log.EventId.Id);
            Assert.Null(log.Exception);

            var rendered = log.Message + log.Exception;

            Assert.DoesNotContain(privateKey, rendered, StringComparison.Ordinal);
            Assert.DoesNotContain("private-cache-value", rendered, StringComparison.Ordinal);
            Assert.DoesNotContain(password, rendered, StringComparison.Ordinal);
            Assert.DoesNotContain(connectionString, rendered, StringComparison.Ordinal);

            Assert.Equal(
                1L,
                await store
                    .CountExpiredAsync()
                    .ConfigureAwait(false));

            Assert.Equal(
                privateValue,
                await cache
                    .GetAsync(privateKey, CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.Single(logger.Entries);

            await store
                .ExecuteAsync($"GRANT DELETE ON {store.QualifiedTableName} TO '{userName}'@'%';")
                .ConfigureAwait(false);

            time.Advance(TimeSpan.FromMinutes(5));

            Assert.Equal(
                privateValue,
                await cache
                    .GetAsync(privateKey, CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.Single(logger.Entries);

            Assert.Equal(
                0L,
                await store
                    .CountExpiredAsync()
                    .ConfigureAwait(false));
        }
        finally
        {
            await store
                .ExecuteAsync($"DROP USER IF EXISTS '{userName}'@'%';")
                .ConfigureAwait(false);
        }
    }
}
