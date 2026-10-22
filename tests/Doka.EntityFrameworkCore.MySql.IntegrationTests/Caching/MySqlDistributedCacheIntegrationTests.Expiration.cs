namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Verifies server-clock expiration and refresh behavior.</summary>
public sealed partial class MySqlDistributedCacheIntegrationTests
{
    /// <summary>Verifies the default expiration against server time.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Default_expiration_uses_server_time(
        string targetName
    )
    {
        // Arrange
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        var cache = store.Cache;

        var before = await store
            .ReadUtcNowAsync()
            .ConfigureAwait(false);

        // Act
        // WHY: Default expiration is verified through the synchronous Set path.
        // ReSharper disable once MethodHasAsyncOverload
        cache.Set("default-expiration", [1], new DistributedCacheEntryOptions());

        var after = await store
            .ReadUtcNowAsync()
            .ConfigureAwait(false);

        var defaults = await store
            .ReadEntryAsync("default-expiration")
            .ConfigureAwait(false);

        // Assert
        Assert.Equal(20 * 60 * 1_000_000L, defaults.SlidingMicroseconds);
        Assert.Null(defaults.AbsoluteExpiration);
        Assert.InRange(defaults.ExpiresAt, before.AddMinutes(20), after.AddMinutes(20));
    }

    /// <summary>Verifies that offset timestamps persist as absolute UTC expiration.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SyncAsyncCases))]
    public async Task Absolute_expiration_is_normalized_to_utc(
        string targetName,
        bool useAsync
    )
    {
        // Arrange
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        var cache = store.Cache;
        var after = await store
            .ReadUtcNowAsync()
            .ConfigureAwait(false);

        var absolute =
            new DateTimeOffset(DateTime.SpecifyKind(after.AddHours(1), DateTimeKind.Utc)).ToOffset(
                TimeSpan.FromHours(7));

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

        // Act
        if (useAsync)
        {
            await cache.RefreshAsync("absolute", CancellationToken.None).ConfigureAwait(false);
        }
        else
        {
            // WHY: This theory row verifies absolute expiration through synchronous Refresh.
            // ReSharper disable once MethodHasAsyncOverload
            cache.Refresh("absolute");
        }

        var value = await cache
            .GetAsync("absolute", CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        Assert.Equal(absolute.UtcDateTime, absoluteState.ExpiresAt);
        Assert.Equal(absolute.UtcDateTime, absoluteState.AbsoluteExpiration);
        Assert.Null(absoluteState.SlidingMicroseconds);
        Assert.Equal(new byte[] { 2 }, value);

        Assert.Equal(
            absoluteState,
            await store
                .ReadEntryAsync("absolute")
                .ConfigureAwait(false));
    }

    /// <summary>Verifies relative expiration against the database clock.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Relative_expiration_uses_server_time(
        string targetName
    )
    {
        // Arrange
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        var cache = store.Cache;

        var before = await store
            .ReadUtcNowAsync()
            .ConfigureAwait(false);

        // Act
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

        var after = await store
            .ReadUtcNowAsync()
            .ConfigureAwait(false);

        var relative = await store
            .ReadEntryAsync("relative")
            .ConfigureAwait(false);

        // Assert
        Assert.InRange(relative.ExpiresAt, before.AddMinutes(30), after.AddMinutes(30));
        Assert.Equal(relative.ExpiresAt, relative.AbsoluteExpiration);
        Assert.Null(relative.SlidingMicroseconds);
    }

    /// <summary>Verifies that reads extend sliding expiration.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Sliding_reads_refresh_expiration(
        string targetName
    )
    {
        // Arrange
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        var cache = store.Cache;

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

        var before = await store
            .ReadUtcNowAsync()
            .ConfigureAwait(false);

        // Act
        // WHY: This case verifies sliding-expiration renewal through synchronous Get.
        // ReSharper disable once MethodHasAsyncOverload
        var value = cache.Get("sliding");

        var after = await store
            .ReadUtcNowAsync()
            .ConfigureAwait(false);

        var refreshed = await store
            .ReadEntryAsync("sliding")
            .ConfigureAwait(false);

        // Assert
        Assert.Equal(new byte[] { 4 }, value);
        Assert.InRange(refreshed.ExpiresAt, before.AddMinutes(10), after.AddMinutes(10));
    }

    /// <summary>Verifies that absolute expiration caps sliding refresh.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(CappedSlidingCases))]
    public async Task Absolute_expiration_caps_sliding_refresh(
        string targetName,
        bool useTryGet
    )
    {
        // Arrange
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        var cache = store.Cache;

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

        var buffer = new ExactBufferWriter(1);
        bool? found = null;

        // Act
        if (useTryGet)
        {
            found = await cache.TryGetAsync("capped", buffer, CancellationToken.None).ConfigureAwait(false);
        }
        else
        {
            await cache.RefreshAsync("capped", CancellationToken.None).ConfigureAwait(false);
        }

        // Assert
        Assert.Equal(capped.AbsoluteExpiration, capped.ExpiresAt);

        if (useTryGet)
        {
            Assert.True(found);
        }

        Assert.Equal(
            capped,
            await store
                .ReadEntryAsync("capped")
                .ConfigureAwait(false));
    }

    /// <summary>Verifies that an expired row is not returned by Get.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SyncAsyncCases))]
    public async Task Expired_get_returns_null(
        string targetName,
        bool useAsync
    )
    {
        // Arrange
        var store = await fixture
            .GetExpirationStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);
        await using var cache = store.CreateCache(new CacheManualTimeProvider());
        var key = useAsync ? "expired-get-async" : "expired-get-sync";
        await CreateExpiredSlidingEntryAsync(store, cache, key)
            .ConfigureAwait(false);

        // Act
        var result = useAsync
            ? await cache
                .GetAsync(key, CancellationToken.None)
                .ConfigureAwait(false)
            // WHY: The false theory row verifies expired reads through synchronous Get.
            // ReSharper disable once MethodHasAsyncOverload
            : cache.Get(key);

        // Assert
        Assert.Null(result);
    }

    /// <summary>Verifies that an expired row does not request a destination buffer.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SyncAsyncCases))]
    public async Task Expired_try_get_does_not_request_a_buffer(
        string targetName,
        bool useAsync
    )
    {
        // Arrange
        var store = await fixture
            .GetExpirationStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);
        await using var cache = store.CreateCache(new CacheManualTimeProvider());
        var key = useAsync ? "expired-buffer-async" : "expired-buffer-sync";
        var destination = new ExactBufferWriter(0);
        await CreateExpiredSlidingEntryAsync(store, cache, key)
            .ConfigureAwait(false);

        // Act
        var found = useAsync
            ? await cache
                .TryGetAsync(key, destination, CancellationToken.None)
                .ConfigureAwait(false)
            // WHY: The false theory row verifies expired reads through synchronous TryGet.
            // ReSharper disable once MethodHasAsyncOverload
            : cache.TryGet(key, destination);

        // Assert
        Assert.False(found);
        Assert.Equal(0, destination.BufferRequests);
    }

    /// <summary>Verifies that refreshing an expired row does not extend its lifetime.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SyncAsyncCases))]
    public async Task Expired_refresh_does_not_extend_the_row(
        string targetName,
        bool useAsync
    )
    {
        // Arrange
        var store = await fixture
            .GetExpirationStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);
        await using var cache = store.CreateCache(new CacheManualTimeProvider());
        var key = useAsync ? "expired-refresh-async" : "expired-refresh-sync";
        var expired = await CreateExpiredSlidingEntryAsync(store, cache, key)
            .ConfigureAwait(false);

        // Act
        if (useAsync)
        {
            await cache
                .RefreshAsync(key, CancellationToken.None)
                .ConfigureAwait(false);
        }
        else
        {
            // WHY: The false theory row verifies expired rows through synchronous Refresh.
            // ReSharper disable once MethodHasAsyncOverload
            cache.Refresh(key);
        }

        // Assert
        Assert.Equal(
            expired,
            await store
                .ReadEntryAsync(key)
                .ConfigureAwait(false));
    }

    /// <summary>Verifies that a write can replace an expired row.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Expired_rows_can_be_replaced(
        string targetName
    )
    {
        // Arrange
        var store = await fixture
            .GetExpirationStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);
        await using var cache = store.CreateCache(new CacheManualTimeProvider());
        const string key = "expired-replace";
        await CreateExpiredSlidingEntryAsync(store, cache, key)
            .ConfigureAwait(false);

        // Act
        await cache
            .SetAsync(key, [6], new DistributedCacheEntryOptions(), CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        // WHY: Readback verifies the synchronous Get path after replacing the expired row.
        // ReSharper disable once MethodHasAsyncOverload
        Assert.Equal(new byte[] { 6 }, cache.Get(key));
    }

    /// <summary>Verifies that an absolute expiration already in the past is unreadable.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Past_absolute_expiration_is_unreadable(
        string targetName
    )
    {
        // Arrange
        var store = await fixture
            .GetExpirationStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);
        await using var cache = store.CreateCache(new CacheManualTimeProvider());
        var before = await store
            .ReadUtcNowAsync()
            .ConfigureAwait(false);
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = new DateTimeOffset(DateTime.SpecifyKind(before.AddHours(-1), DateTimeKind.Utc)),
        };

        // Act
        await cache
            .SetAsync("already-expired", [7], options, CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        Assert.Null(
            await cache
                .GetAsync("already-expired", CancellationToken.None)
                .ConfigureAwait(false));
    }

    private static async Task<CacheIntegrationStore.EntryState> CreateExpiredSlidingEntryAsync(
        CacheIntegrationStore store,
        MySqlDistributedCache cache,
        string key
    )
    {
        await cache
            .SetAsync(
                key,
                [4],
                new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(10) },
                CancellationToken.None)
            .ConfigureAwait(false);

        await store
            .ExecuteForKeyAsync(
                $"UPDATE {store.QualifiedTableName} SET `ExpiresAtUtc` = TIMESTAMPADD(SECOND, -1, UTC_TIMESTAMP(6)) "
                + "WHERE `Id` = CAST(@key AS BINARY);",
                key)
            .ConfigureAwait(false);

        return await store
            .ReadEntryAsync(key)
            .ConfigureAwait(false);
    }
}
