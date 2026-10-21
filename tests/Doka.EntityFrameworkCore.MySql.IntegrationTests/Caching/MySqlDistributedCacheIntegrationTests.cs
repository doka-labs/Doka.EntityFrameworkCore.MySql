namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Verifies distributed-cache behavior on every supported live engine.</summary>
/// <param name="fixture">The lazily deployed per-engine cache stores.</param>
[IntegrationDatabaseTest]
public sealed class MySqlDistributedCacheIntegrationTests(MySqlDistributedCacheFixture fixture)
    : IClassFixture<MySqlDistributedCacheFixture>
{
    private const int CleanupProbeConnectionCount = 2;
    private const int ConcurrentUpsertProbeCount = 16;
    private static readonly int[] s_payloadSizes = [0, 23, 8192, 1024 * 1024];

    private static readonly string[] s_cleanupInitiators =
    [
        "get",
        "try-get",
        "set-array",
        "set-sequence",
        "set-segmented",
        "refresh",
        "remove",
    ];

    private static readonly string[] s_waitedRefreshOperations =
    [
        "refresh-sync",
        "refresh-async",
        "get-sync",
        "get-async",
        "try-get-sync",
        "try-get-async",
    ];

    /// <summary>Returns one independently reported test case per selected database target.</summary>
    // WHY: xUnit v2 cannot skip individual InlineData rows at runtime. Select the
    // engine rows during discovery while keeping each selected engine visible in Rider.
    public static IEnumerable<object[]> SelectedTargets =>
        IntegrationTestEnvironment
            .GetSelectedTargets()
            .Where(target => IntegrationTestEnvironment
                .GetSupportedTargets()
                .Contains(target))
            .Select(target => new object[] { target.ToString() });

    /// <summary>Returns the selected engines crossed with all byte payload sizes.</summary>
    public static IEnumerable<object[]> BytePayloadCases =>
        SelectedTargets.SelectMany(target => s_payloadSizes.Select(size => new[] { target[0], size }));

    /// <summary>Returns sizes large enough to exercise multi-segment payloads.</summary>
    public static IEnumerable<object[]> SegmentedBytePayloadCases =>
        SelectedTargets.SelectMany(target => s_payloadSizes
            .Where(size => size > 1)
            .Select(size => new[] { target[0], size }));

    /// <summary>Returns each selected engine crossed with an independent cleanup initiator.</summary>
    public static IEnumerable<object[]> CleanupCancellationCases =>
        SelectedTargets.SelectMany(target =>
            s_cleanupInitiators.Select(initiator => new[] { target[0], initiator }));

    /// <summary>Returns each selected engine with one synchronous or asynchronous cache path.</summary>
    public static IEnumerable<object[]> SyncAsyncCases =>
        SelectedTargets.SelectMany(target =>
            new[] { new[] { target[0], false }, new[] { target[0], true }, });

    /// <summary>Returns each selected engine with one sliding-read outcome.</summary>
    public static IEnumerable<object[]> SlidingLockWaitCases =>
        SelectedTargets.SelectMany(target => new[]
        {
            new[] { target[0], false, false },
            new[] { target[0], false, true },
            new[] { target[0], true, false },
            new[] { target[0], true, true },
        });

    /// <summary>Returns each selected engine with one lock-waiting cache operation.</summary>
    public static IEnumerable<object[]> WaitedRefreshCases =>
        SelectedTargets.SelectMany(target =>
            s_waitedRefreshOperations.Select(operation => new object[] { target[0], operation }));

    /// <summary>Returns each selected engine with one table-lock cancellation initiator.</summary>
    public static IEnumerable<object[]> LockedCancellationCases => CleanupCancellationCases;

    /// <summary>Verifies explicit cache deployment and repeated schema application.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Deployment_is_explicit_and_idempotent(
        string targetName
    )
    {
        await using var store = await CacheIntegrationStore
            .CreateAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        await AssertExplicitIdempotentDeploymentAsync(store)
            .ConfigureAwait(false);
    }

    /// <summary>Verifies that quoted identifiers remain safe in cache commands.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Quoted_table_identifiers_are_supported(
        string targetName
    )
    {
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        await AssertQuotedTableIdentifierAsync(store)
            .ConfigureAwait(false);
    }

    /// <summary>Verifies that independent cache tables do not share entries.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Deployment_tables_are_isolated(
        string targetName
    )
    {
        var target = ParseTarget(targetName);
        var store = await fixture
            .GetStoreAsync(target)
            .ConfigureAwait(false);

        await AssertDeploymentTableIsolationAsync(store, target)
            .ConfigureAwait(false);
    }

    /// <summary>Verifies that reading an absent key returns no value.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SyncAsyncCases))]
    public async Task Missing_get_returns_null(
        string targetName,
        bool useAsync
    )
    {
        // Arrange
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        // Act
        var value = useAsync
            ? await store
                .Cache
                .GetAsync("missing", CancellationToken.None)
                .ConfigureAwait(false)
            // WHY: The false theory row verifies the synchronous Get contract.
            // ReSharper disable once MethodHasAsyncOverload
            : store.Cache.Get("missing");

        // Assert
        Assert.Null(value);
    }

    /// <summary>Verifies that an absent key does not request a destination buffer.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SyncAsyncCases))]
    public async Task Missing_try_get_does_not_request_a_buffer(
        string targetName,
        bool useAsync
    )
    {
        // Arrange
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);
        var writer = new ExactBufferWriter(0);

        // Act
        var found = useAsync
            ? await store
                .Cache
                .TryGetAsync("missing", writer, CancellationToken.None)
                .ConfigureAwait(false)
            // WHY: The false theory row verifies the synchronous buffer-read contract.
            // ReSharper disable once MethodHasAsyncOverload
            : store.Cache.TryGet("missing", writer);

        // Assert
        Assert.False(found);
        Assert.Equal(0, writer.BufferRequests);
        Assert.Equal(0, writer.WrittenCount);
    }

    /// <summary>Verifies that refreshing an absent key succeeds without creating it.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SyncAsyncCases))]
    public async Task Missing_refresh_is_a_no_op(
        string targetName,
        bool useAsync
    )
    {
        // Arrange
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        // Act
        if (useAsync)
        {
            await store
                .Cache
                .RefreshAsync("missing", CancellationToken.None)
                .ConfigureAwait(false);
        }
        else
        {
            // WHY: The false theory row verifies the synchronous Refresh contract.
            // ReSharper disable once MethodHasAsyncOverload
            store.Cache.Refresh("missing");
        }

        // Assert
        // WHY: The synchronous read confirms the no-op across both Refresh variants.
        // ReSharper disable once MethodHasAsyncOverload
        Assert.Null(store.Cache.Get("missing"));
    }

    /// <summary>Verifies that removing an absent key succeeds without creating it.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SyncAsyncCases))]
    public async Task Missing_remove_is_a_no_op(
        string targetName,
        bool useAsync
    )
    {
        // Arrange
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        // Act
        if (useAsync)
        {
            await store
                .Cache
                .RemoveAsync("missing", CancellationToken.None)
                .ConfigureAwait(false);
        }
        else
        {
            // WHY: The false theory row verifies the synchronous Remove contract.
            // ReSharper disable once MethodHasAsyncOverload
            store.Cache.Remove("missing");
        }

        // Assert
        // WHY: The synchronous read confirms the no-op across both Remove variants.
        // ReSharper disable once MethodHasAsyncOverload
        Assert.Null(store.Cache.Get("missing"));
    }

    /// <summary>Verifies synchronous byte-array writes and reads.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(BytePayloadCases))]
    public async Task Synchronous_byte_sets_round_trip_through_synchronous_get(
        string targetName,
        int size
    )
    {
        // Arrange
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);
        var value = CreatePayload(size);
        var key = $"sync-byte-sync-get-{size}";

        // Act
        // WHY: This case verifies both synchronous byte-array entry points.
        // ReSharper disable once MethodHasAsyncOverload
        store.Cache.Set(key, value, new DistributedCacheEntryOptions());
        // WHY: The same case also verifies the synchronous Get entry point.
        // ReSharper disable once MethodHasAsyncOverload
        var actual = store.Cache.Get(key);

        // Assert
        Assert.Equal(value, actual);
    }

    /// <summary>Verifies asynchronous reads of synchronously written byte arrays.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(BytePayloadCases))]
    public async Task Synchronous_byte_sets_round_trip_through_asynchronous_get(
        string targetName,
        int size
    )
    {
        // Arrange
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);
        var value = CreatePayload(size);
        var key = $"sync-byte-async-get-{size}";

        // Act
        // WHY: The write remains synchronous to verify cross-mode readback.
        // ReSharper disable once MethodHasAsyncOverload
        store.Cache.Set(key, value, new DistributedCacheEntryOptions());
        var actual = await store
            .Cache
            .GetAsync(key, CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        Assert.Equal(value, actual);
    }

    /// <summary>Verifies synchronous reads of asynchronously written byte arrays.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(BytePayloadCases))]
    public async Task Asynchronous_byte_sets_round_trip_through_synchronous_get(
        string targetName,
        int size
    )
    {
        // Arrange
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);
        var value = CreatePayload(size);
        var key = $"async-byte-sync-get-{size}";

        // Act
        await store
            .Cache
            .SetAsync(key, value, new DistributedCacheEntryOptions(), CancellationToken.None)
            .ConfigureAwait(false);

        // WHY: The read remains synchronous to verify cross-mode readback.
        // ReSharper disable once MethodHasAsyncOverload
        var actual = store.Cache.Get(key);

        // Assert
        Assert.Equal(value, actual);
    }

    /// <summary>Verifies synchronous buffer reads, including empty values.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(BytePayloadCases))]
    public async Task Synchronous_try_get_preserves_byte_values(
        string targetName,
        int size
    )
    {
        // Arrange
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);
        var value = CreatePayload(size);
        var key = $"sync-buffer-get-{size}";
        var destination = new ExactBufferWriter(size);

        await store
            .Cache
            .SetAsync(key, value, new DistributedCacheEntryOptions(), CancellationToken.None)
            .ConfigureAwait(false);

        // Act
        // WHY: This case exercises the synchronous buffer-read API.
        // ReSharper disable once MethodHasAsyncOverload
        var found = store.Cache.TryGet(key, destination);

        // Assert
        Assert.True(found);
        Assert.Equal(value, destination.WrittenBytes);

        if (size == 0)
        {
            Assert.Equal(0, destination.BufferRequests);
        }
    }

    /// <summary>Verifies asynchronous buffer reads, including empty values.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(BytePayloadCases))]
    public async Task Asynchronous_try_get_preserves_byte_values(
        string targetName,
        int size
    )
    {
        // Arrange
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);
        var value = CreatePayload(size);
        var key = $"async-buffer-get-{size}";
        var destination = new ExactBufferWriter(size);

        await store
            .Cache
            .SetAsync(key, value, new DistributedCacheEntryOptions(), CancellationToken.None)
            .ConfigureAwait(false);

        // Act
        var found = await store
            .Cache
            .TryGetAsync(key, destination, CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        Assert.True(found);
        Assert.Equal(value, destination.WrittenBytes);

        if (size == 0)
        {
            Assert.Equal(0, destination.BufferRequests);
        }
    }

    /// <summary>Verifies synchronous removal of byte-array entries.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(BytePayloadCases))]
    public async Task Synchronous_remove_deletes_byte_entries(
        string targetName,
        int size
    )
    {
        // Arrange
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);
        var key = $"sync-byte-remove-{size}";

        await store
            .Cache
            .SetAsync(key, CreatePayload(size), new DistributedCacheEntryOptions(), CancellationToken.None)
            .ConfigureAwait(false);

        // Act
        // WHY: This case exercises the synchronous Remove API.
        // ReSharper disable once MethodHasAsyncOverload
        store.Cache.Remove(key);

        // Assert
        Assert.Null(
            await store
                .Cache
                .GetAsync(key, CancellationToken.None)
                .ConfigureAwait(false));
    }

    /// <summary>Verifies asynchronous removal of byte-array entries.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(BytePayloadCases))]
    public async Task Asynchronous_remove_deletes_byte_entries(
        string targetName,
        int size
    )
    {
        // Arrange
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);
        var key = $"async-byte-remove-{size}";

        await store
            .Cache
            .SetAsync(key, CreatePayload(size), new DistributedCacheEntryOptions(), CancellationToken.None)
            .ConfigureAwait(false);

        // Act
        await store
            .Cache
            .RemoveAsync(key, CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        // WHY: Readback also verifies the synchronous Get path after asynchronous removal.
        // ReSharper disable once MethodHasAsyncOverload
        Assert.Null(store.Cache.Get(key));
    }

    /// <summary>Verifies synchronous contiguous-sequence writes through an asynchronous read.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(BytePayloadCases))]
    public async Task Synchronous_contiguous_sequence_sets_preserve_values(
        string targetName,
        int size
    )
    {
        // Arrange
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);
        var value = CreatePayload(size);
        var key = $"sync-contiguous-set-{size}";
        var sequence = new ReadOnlySequence<byte>(value);

        // Act
        // WHY: This case exercises the synchronous ReadOnlySequence write API.
        // ReSharper disable once MethodHasAsyncOverload
        store.Cache.Set(key, sequence, new DistributedCacheEntryOptions());
        var actual = await store
            .Cache
            .GetAsync(key, CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        Assert.Equal(value, actual);
    }

    /// <summary>Verifies asynchronous contiguous-sequence writes through a synchronous read.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(BytePayloadCases))]
    public async Task Asynchronous_contiguous_sequence_sets_preserve_values(
        string targetName,
        int size
    )
    {
        // Arrange
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);
        var value = CreatePayload(size);
        var key = $"async-contiguous-set-{size}";
        var sequence = new ReadOnlySequence<byte>(value);

        // Act
        await store
            .Cache
            .SetAsync(key, sequence, new DistributedCacheEntryOptions(), CancellationToken.None)
            .ConfigureAwait(false);

        // WHY: This case verifies synchronous readback of an asynchronous sequence write.
        // ReSharper disable once MethodHasAsyncOverload
        var actual = store.Cache.Get(key);

        // Assert
        Assert.Equal(value, actual);
    }

    /// <summary>Verifies synchronous segmented-sequence writes through an asynchronous read.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SegmentedBytePayloadCases))]
    public async Task Synchronous_segmented_sequence_sets_preserve_values(
        string targetName,
        int size
    )
    {
        // Arrange
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);
        var value = CreatePayload(size);
        var key = $"sync-segmented-set-{size}";
        var sequence = CreateSegmentedSequence(value);

        // Act
        // WHY: This case exercises the synchronous segmented-sequence write API.
        // ReSharper disable once MethodHasAsyncOverload
        store.Cache.Set(key, sequence, new DistributedCacheEntryOptions());
        var actual = await store
            .Cache
            .GetAsync(key, CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        Assert.Equal(value, actual);
    }

    /// <summary>Verifies asynchronous segmented-sequence writes through a synchronous read.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SegmentedBytePayloadCases))]
    public async Task Asynchronous_segmented_sequence_sets_preserve_values(
        string targetName,
        int size
    )
    {
        // Arrange
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);
        var value = CreatePayload(size);
        var key = $"async-segmented-set-{size}";
        var sequence = CreateSegmentedSequence(value);

        // Act
        await store
            .Cache
            .SetAsync(key, sequence, new DistributedCacheEntryOptions(), CancellationToken.None)
            .ConfigureAwait(false);

        // WHY: This case verifies synchronous readback of an asynchronous segmented write.
        // ReSharper disable once MethodHasAsyncOverload
        var actual = store.Cache.Get(key);

        // Assert
        Assert.Equal(value, actual);
    }

    /// <summary>Verifies that binary cache keys retain ordinal identity.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Keys_are_ordinal(
        string targetName
    )
    {
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        await AssertOrdinalKeysAsync(store)
            .ConfigureAwait(false);
    }

    /// <summary>Verifies rejection of cache keys that exceed the UTF-8 byte limit.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Oversized_utf8_keys_are_rejected(
        string targetName
    )
    {
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        await AssertOversizedUtf8KeysAsync(store)
            .ConfigureAwait(false);
    }

    /// <summary>Verifies rejection of malformed UTF-16 cache keys.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Malformed_utf16_keys_are_rejected(
        string targetName
    )
    {
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        AssertMalformedUtf16Keys(store);
    }

    /// <summary>Verifies the default expiration against server time.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Default_expiration_uses_server_time(
        string targetName
    )
    {
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        await AssertDefaultExpirationAsync(store)
            .ConfigureAwait(false);
    }

    /// <summary>Verifies that offset timestamps persist as absolute UTC expiration.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Absolute_expiration_is_normalized_to_utc(
        string targetName
    )
    {
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        await AssertAbsoluteExpirationAsync(store)
            .ConfigureAwait(false);
    }

    /// <summary>Verifies relative expiration against the database clock.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Relative_expiration_uses_server_time(
        string targetName
    )
    {
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        await AssertRelativeExpirationAsync(store)
            .ConfigureAwait(false);
    }

    /// <summary>Verifies that reads extend sliding expiration.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Sliding_reads_refresh_expiration(
        string targetName
    )
    {
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        await AssertSlidingExpirationAsync(store)
            .ConfigureAwait(false);
    }

    /// <summary>Verifies that absolute expiration caps sliding refresh.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Absolute_expiration_caps_sliding_refresh(
        string targetName
    )
    {
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        await AssertCappedSlidingExpirationAsync(store)
            .ConfigureAwait(false);
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

    /// <summary>Verifies that concurrent upserts preserve whole values.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Concurrent_upserts_preserve_whole_values(
        string targetName
    )
    {
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        await AssertConcurrentUpsertsAsync(store)
            .ConfigureAwait(false);
    }

    /// <summary>Verifies removal wins a race with refresh without resurrecting the entry.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Concurrent_remove_and_refresh_do_not_resurrect_entries(
        string targetName
    )
    {
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        await AssertConcurrentRemoveRefreshAsync(store)
            .ConfigureAwait(false);
    }

    /// <summary>Verifies a synchronous buffer read against concurrent replacement.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Synchronous_buffer_reads_preserve_snapshot_during_replacement(
        string targetName
    )
    {
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        await AssertReadReplacementRaceAsync(store, useAsync: false)
            .ConfigureAwait(false);
    }

    /// <summary>Verifies an asynchronous buffer read against concurrent replacement.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Asynchronous_buffer_reads_preserve_snapshot_during_replacement(
        string targetName
    )
    {
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        await AssertReadReplacementRaceAsync(store, useAsync: true)
            .ConfigureAwait(false);
    }

    /// <summary>Verifies a synchronous buffer read against concurrent removal.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Synchronous_buffer_reads_preserve_snapshot_during_removal(
        string targetName
    )
    {
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        await AssertReadRemovalRaceAsync(store, useAsync: false)
            .ConfigureAwait(false);
    }

    /// <summary>Verifies an asynchronous buffer read against concurrent removal.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Asynchronous_buffer_reads_preserve_snapshot_during_removal(
        string targetName
    )
    {
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        await AssertReadRemovalRaceAsync(store, useAsync: true)
            .ConfigureAwait(false);
    }

    /// <summary>Verifies that ambient rollback does not roll back cache writes.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SyncAsyncCases))]
    public async Task Ambient_transactions_do_not_own_cache_writes(
        string targetName,
        bool useAsync
    )
    {
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        await AssertAmbientTransactionsDoNotOwnCacheWritesAsync(store, useAsync)
            .ConfigureAwait(false);
    }

    /// <summary>Verifies that a supplied data source runs caller-owned connection hooks.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task External_data_sources_run_caller_connection_hooks(
        string targetName
    )
    {
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        await AssertExternalDataSourceConnectionHooksAsync(store)
            .ConfigureAwait(false);
    }

    /// <summary>Verifies that a supplied data source shares its caller-owned pool.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task External_data_sources_share_the_caller_pool(
        string targetName
    )
    {
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        await AssertExternalDataSourcePoolAsync(store)
            .ConfigureAwait(false);
    }

    /// <summary>Verifies that DI disposal does not dispose a caller-owned data source.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task External_data_sources_survive_provider_disposal(
        string targetName
    )
    {
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        await AssertExternalDataSourceSurvivesProviderDisposalAsync(store)
            .ConfigureAwait(false);
    }

    /// <summary>Verifies that one lock-waiting operation cannot resurrect an expired entry.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(WaitedRefreshCases))]
    public async Task Waited_refresh_does_not_resurrect_expired_entries(
        string targetName,
        string operation
    )
    {
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        await AssertWaitedRefreshDoesNotResurrectAsync(store, operation)
            .ConfigureAwait(false);
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
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        await AssertSlidingLockWaitCompletionAndCancellationAsync(store, useTryGet, cancel)
            .ConfigureAwait(false);
    }

    /// <summary>Verifies cancellation of one public cache operation under a table lock.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(LockedCancellationCases))]
    public async Task Locked_cache_operations_observe_cancellation(
        string targetName,
        string initiator
    )
    {
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        await AssertCancellationAsync(store, initiator)
            .ConfigureAwait(false);
    }

    /// <summary>Verifies the cleanup interval, 1,000-row bound, and continuation.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Cleanup_observes_interval_and_batch_limit(
        string targetName
    )
    {
        await using var store = await CreateDeployedStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        await AssertCleanupIntervalAndBatchLimitAsync(store)
            .ConfigureAwait(false);
    }

    /// <summary>Verifies one canceled cleanup initiator and its subsequent recovery.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(CleanupCancellationCases))]
    public async Task Canceled_cleanup_recovers_for_initiator(
        string targetName,
        string initiator
    )
    {
        var target = ParseTarget(targetName);
        await using var store = await CreateDeployedStoreAsync(target)
            .ConfigureAwait(false);

        await AssertCanceledCleanupAndContinuationAsync(store, initiator)
            .ConfigureAwait(false);
    }

    /// <summary>Verifies DML-only access and nonfatal cleanup permission failures.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Dml_only_access_survives_cleanup_permission_failures(
        string targetName
    )
    {
        await using var store = await CreateDeployedStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        await AssertDmlOnlyPermissionsAndCleanupFailureAsync(store)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Binary process text cannot make the lock probe fail or conceal its ASCII command match.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public async Task Locking_probe_handles_binary_process_text_without_losing_its_match()
    {
        await using var connection = new MySqlConnection(
            IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql97));
        await connection.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = GetLockingStatementProbeSql(
            "(SELECT CONNECTION_ID() + 1 AS ID, @statement AS INFO) AS process");
        var statement = Encoding
            .ASCII
            .GetBytes("UPDATE `DokaProbe` SET `Value` = ")
            .Concat(
                new byte[]
                {
                    0x88,
                    0x99,
                    0xAA,
                    0xBB,
                    0xCC,
                    0xDD
                })
            .ToArray();
        command.Parameters.AddWithValue("@statement", statement);
        command.Parameters.AddWithValue("@tableName", "DokaProbe");
        command.Parameters.AddWithValue("@cleanupOnly", false);

        var waiting = Convert.ToInt64(
            await command.ExecuteScalarAsync(CancellationToken.None),
            CultureInfo.InvariantCulture);

        Assert.Equal(1, waiting);
    }

    /// <summary>
    /// Binary process text also remains searchable while observing a table-lock wait.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public async Task Table_lock_probe_handles_binary_process_text_without_losing_its_match()
    {
        await using var connection = new MySqlConnection(
            IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql97));
        await connection.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = GetWaitingForTableLockProbeSql(
            "(SELECT CONNECTION_ID() + 1 AS ID, @statement AS INFO, "
            + "'Waiting for table metadata lock' AS STATE) AS process");
        var statement = Encoding
            .ASCII
            .GetBytes("ALTER TABLE `DokaProbe` ADD `Value` int ")
            .Concat(
                new byte[]
                {
                    0x88,
                    0x99,
                    0xAA,
                    0xBB,
                    0xCC,
                    0xDD
                })
            .ToArray();
        command.Parameters.AddWithValue("@statement", statement);
        command.Parameters.AddWithValue("@tableName", "DokaProbe");

        var waiting = Convert.ToInt64(
            await command.ExecuteScalarAsync(CancellationToken.None),
            CultureInfo.InvariantCulture);

        Assert.Equal(1, waiting);
    }

    private static IntegrationDatabaseTarget ParseTarget(
        string targetName
    ) => Enum.Parse<IntegrationDatabaseTarget>(targetName);

    private static async Task<CacheIntegrationStore> CreateDeployedStoreAsync(
        IntegrationDatabaseTarget target
    )
    {
        var store = await CacheIntegrationStore
            .CreateAsync(target)
            .ConfigureAwait(false);

        try
        {
            await store
                .ExecuteAsync(MySqlCacheSchema.GetCreateScript(store.SchemaName, store.TableName))
                .ConfigureAwait(false);

            return store;
        }
        catch
        {
            await store
                .DisposeAsync()
                .ConfigureAwait(false);

            throw;
        }
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

    private static byte[] CreatePayload(
        int size
    )
    {
        var value = new byte[size];

        // WHY: A nonuniform payload catches truncation and offset errors at the 1 MiB boundary.
        for (var index = 0; index < value.Length; index++)
        {
            value[index] = (byte)(index % 251);
        }

        return value;
    }

    private static ReadOnlySequence<byte> CreateSegmentedSequence(
        byte[] value
    )
    {
        var first = new CacheSequenceSegment(value.AsMemory(0, value.Length / 2));
        var last = first.Append(value.AsMemory(value.Length / 2));

        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private static async Task AssertSlidingLockWaitCompletionAndCancellationAsync(
        CacheIntegrationStore store,
        bool useTryGet,
        bool cancel
    )
    {
        // Arrange
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

        // Act
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

                await Assert
                    .ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(
                        TimeSpan.FromSeconds(15),
                        CancellationToken.None))
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

                // Assert
                if (useTryGet)
                {
                    Assert.True(await ((Task<bool>)pending).ConfigureAwait(false));
                    Assert.Equal(new byte[] { 1 }, destination.WrittenBytes);
                }
                else
                {
                    Assert.Equal(new byte[] { 1 }, await ((Task<byte[]?>)pending).ConfigureAwait(false));
                }

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
                    .ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(
                        TimeSpan.FromSeconds(15),
                        CancellationToken.None))
                    .ConfigureAwait(false);
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

        Assert.Equal(
            new byte[] { 0 },
            await store
                .Cache
                .GetAsync("case", CancellationToken.None)
                .ConfigureAwait(false));
    }

    private static async Task AssertOversizedUtf8KeysAsync(
        CacheIntegrationStore store
    )
    {
        // Arrange
        var oversizedKey = new string('\u00e9', 513);
        const string validKey = "valid-after-oversized";

        await store
            .Cache
            .SetAsync(validKey, [1], new DistributedCacheEntryOptions(), CancellationToken.None)
            .ConfigureAwait(false);

        // Act
        var oversizedFailure = await Record
            .ExceptionAsync(() => store.Cache.SetAsync(
                oversizedKey,
                [1],
                new DistributedCacheEntryOptions(),
                CancellationToken.None))
            .ConfigureAwait(false);

        // Assert
        Assert.IsType<ArgumentException>(oversizedFailure);
        Assert.Equal(
            new byte[] { 1 },
            await store
                .Cache
                .GetAsync(validKey, CancellationToken.None)
                .ConfigureAwait(false));
    }

    private static void AssertMalformedUtf16Keys(
        CacheIntegrationStore store
    )
    {
        // Arrange
        const string malformedKey = "invalid\ud800";

        // Act
        // WHY: Malformed-key validation must also reject the synchronous Get path.
        // ReSharper disable once MethodHasAsyncOverload
        var malformedFailure = Record.Exception(() => store.Cache.Get(malformedKey));

        // Assert
        Assert.IsType<ArgumentException>(malformedFailure);
    }

    private static async Task AssertDefaultExpirationAsync(
        CacheIntegrationStore store
    )
    {
        // Arrange
        var cache = store.Cache;

        var before = await store
            .ReadUtcNowAsync()
            .ConfigureAwait(false);

        // Act
        // WHY: Default expiration is verified through the synchronous Set path.
        // ReSharper disable once MethodHasAsyncOverload
        cache.Set("default-expiration", [1], new DistributedCacheEntryOptions());

        // Assert
        var after = await store
            .ReadUtcNowAsync()
            .ConfigureAwait(false);

        var defaults = await store
            .ReadEntryAsync("default-expiration")
            .ConfigureAwait(false);

        Assert.Equal(20 * 60 * 1_000_000L, defaults.SlidingMicroseconds);
        Assert.Null(defaults.AbsoluteExpiration);
        Assert.InRange(defaults.ExpiresAt, before.AddMinutes(20), after.AddMinutes(20));
    }

    private static async Task AssertAbsoluteExpirationAsync(
        CacheIntegrationStore store
    )
    {
        // Arrange
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
        // WHY: Absolute expiration must survive a synchronous Refresh as well as an asynchronous one.
        // ReSharper disable once MethodHasAsyncOverload
        cache.Refresh("absolute");

        await cache
            .RefreshAsync("absolute", CancellationToken.None)
            .ConfigureAwait(false);

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

    private static async Task AssertRelativeExpirationAsync(
        CacheIntegrationStore store
    )
    {
        // Arrange
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

        // Assert
        var after = await store
            .ReadUtcNowAsync()
            .ConfigureAwait(false);

        var relative = await store
            .ReadEntryAsync("relative")
            .ConfigureAwait(false);

        Assert.InRange(relative.ExpiresAt, before.AddMinutes(30), after.AddMinutes(30));
        Assert.Equal(relative.ExpiresAt, relative.AbsoluteExpiration);
        Assert.Null(relative.SlidingMicroseconds);
    }

    private static async Task AssertSlidingExpirationAsync(
        CacheIntegrationStore store
    )
    {
        // Arrange
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

        // Assert
        var after = await store
            .ReadUtcNowAsync()
            .ConfigureAwait(false);

        var refreshed = await store
            .ReadEntryAsync("sliding")
            .ConfigureAwait(false);

        Assert.Equal(new byte[] { 4 }, value);
        Assert.InRange(refreshed.ExpiresAt, before.AddMinutes(10), after.AddMinutes(10));
    }

    private static async Task AssertCappedSlidingExpirationAsync(
        CacheIntegrationStore store
    )
    {
        // Arrange
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

        // Act
        await cache
            .RefreshAsync("capped", CancellationToken.None)
            .ConfigureAwait(false);

        var found = await cache
            .TryGetAsync("capped", buffer, CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        Assert.Equal(capped.AbsoluteExpiration, capped.ExpiresAt);
        Assert.True(found);
        Assert.Equal(
            capped,
            await store
                .ReadEntryAsync("capped")
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
    }

    private static async Task AssertConcurrentRemoveRefreshAsync(
        CacheIntegrationStore store
    )
    {
        var concurrentConnectionString = new MySqlConnectionStringBuilder(store.ConnectionString)
        {
            MaximumPoolSize = ConcurrentUpsertProbeCount + 1,
        }.ConnectionString;

        await using var cache = store.CreateCache(TimeProvider.System, connectionString: concurrentConnectionString);

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

    private static async Task AssertReadReplacementRaceAsync(
        CacheIntegrationStore store,
        bool useAsync
    )
    {
        var key = $"read-replacement-race-{useAsync}";

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
                    // WHY: The false variant holds synchronous TryGet across the replacement race.
                    // ReSharper disable once MethodHasAsyncOverload
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
    }

    private static async Task AssertReadRemovalRaceAsync(
        CacheIntegrationStore store,
        bool useAsync
    )
    {
        var key = $"read-removal-race-{useAsync}";

        await store
            .Cache
            .SetAsync(key, [3, 4], new DistributedCacheEntryOptions(), CancellationToken.None)
            .ConfigureAwait(false);

        using var paused = new PausingBufferWriter(2);
        var reading = Task.Run(
            async () => useAsync
                ? await store
                    .Cache
                    .TryGetAsync(key, paused, CancellationToken.None)
                    .ConfigureAwait(false)
                // WHY: The false variant holds synchronous TryGet across the removal race.
                // ReSharper disable once MethodHasAsyncOverload
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
    private static async Task AssertCancellationAsync(
        CacheIntegrationStore store,
        string initiator
    )
    {
        // Arrange
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

    private static async Task AssertWaitedRefreshDoesNotResurrectAsync(
        CacheIntegrationStore store,
        string operation
    )
    {
        // Arrange
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
    private static async Task AssertLockingStatementStartedAsync(
        MySqlConnection observer,
        string tableName,
        Task pending,
        bool cleanupOnly = false
    )
    {
        await using var command = observer.CreateCommand();

        // WHY: RemoveAsync and cleanup both issue DELETE statements. The cleanup-only predicate requires the
        // cleanup command's primary-index clause so cancellation never interrupts foreground removal instead.
        // Unique-key locking reads can wait in optimization before INNODB_TRX exposes the wait.
        command.CommandText = GetLockingStatementProbeSql("information_schema.PROCESSLIST");

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

    // WHY: PROCESSLIST.INFO can expose command bytes that are not valid in its
    // metadata charset. Interpret the bytes without loss before searching for
    // ASCII SQL tokens, so an unrelated request cannot break the lock probe.
    private static string GetLockingStatementProbeSql(
        string processSource
    ) => $"""
           SELECT COUNT(*) FROM (
               SELECT ID, CONVERT(CAST(INFO AS BINARY) USING latin1) AS StatementText
               FROM {processSource}
           ) AS observed
           WHERE ID <> CONNECTION_ID()
               AND LOCATE(@tableName, COALESCE(StatementText, '')) > 0
               AND (UPPER(StatementText) LIKE '%FOR UPDATE%'
                   OR UPPER(LTRIM(StatementText)) LIKE 'UPDATE %'
                   OR UPPER(LTRIM(StatementText)) LIKE 'DELETE %')
               AND (@cleanupOnly = FALSE
                   OR UPPER(LTRIM(StatementText)) LIKE 'DELETE % FROM % FORCE INDEX (PRIMARY)%');
           """;

    private static async Task AssertAmbientTransactionsDoNotOwnCacheWritesAsync(
        CacheIntegrationStore store,
        bool useAsync
    )
    {
        // Arrange
        var key = useAsync ? "ambient-async" : "ambient-sync";

        // Act
        using (var scope = new System.Transactions.TransactionScope(
                   System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
        {
            // WHY: Neither cache path may enlist its write in the ambient transaction.
            if (useAsync)
            {
                await store
                    .Cache
                    .SetAsync(key, [1], new DistributedCacheEntryOptions(), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            else
            {
                // WHY: The false variant verifies that the synchronous cache write ignores the ambient transaction.
                // ReSharper disable once MethodHasAsyncOverload
                store.Cache.Set(key, [1], new DistributedCacheEntryOptions());
            }
        }

        // Assert
        Assert.Equal(
            new byte[] { 1 },
            await store
                .Cache
                .GetAsync(key, CancellationToken.None)
                .ConfigureAwait(false));
    }

    private static async Task AssertWaitingForTableLockAsync(
        MySqlConnection observer,
        string tableName,
        Task pending
    )
    {
        await using var command = observer.CreateCommand();
        command.CommandText = GetWaitingForTableLockProbeSql("information_schema.PROCESSLIST");

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

    // WHY: This observer can encounter the same undecodable process text as
    // the row-lock probe while another test sends binary command parameters.
    private static string GetWaitingForTableLockProbeSql(
        string processSource
    ) => $"""
           SELECT COUNT(*) FROM (
               SELECT ID, CONVERT(CAST(INFO AS BINARY) USING latin1) AS StatementText, STATE
               FROM {processSource}
           ) AS observed
           WHERE ID <> CONNECTION_ID()
               AND LOCATE(@tableName, COALESCE(StatementText, '')) > 0
               AND LOWER(COALESCE(STATE, '')) LIKE '%lock%';
           """;

    private static async Task AssertExternalDataSourceConnectionHooksAsync(
        CacheIntegrationStore store
    )
    {
        // Arrange
        var passwordRequests = 0;
        var connectionOpens = 0;
        await using var dataSource = CreateExternalDataSource(
            store,
            () => Interlocked.Increment(ref passwordRequests),
            () => Interlocked.Increment(ref connectionOpens));

        await using var provider = CreateExternalCacheProvider(store, dataSource);
        var cache = provider.GetRequiredService<IBufferDistributedCache>();

        // Act
        await cache
            .SetAsync("external-hooks", [4, 2], new DistributedCacheEntryOptions(), CancellationToken.None)
            .ConfigureAwait(false);

        var actual = await cache
            .GetAsync("external-hooks", CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        Assert.Equal(new byte[] { 4, 2 }, actual);
        Assert.True(Volatile.Read(ref passwordRequests) > 0);
        Assert.True(Volatile.Read(ref connectionOpens) > 0);
    }

    private static async Task AssertExternalDataSourcePoolAsync(
        CacheIntegrationStore store
    )
    {
        // Arrange
        await using var dataSource = CreateExternalDataSource(store);
        await using var provider = CreateExternalCacheProvider(store, dataSource);
        var cache = provider.GetRequiredService<IBufferDistributedCache>();
        await cache
            .SetAsync("external-pool", [4, 2], new DistributedCacheEntryOptions(), CancellationToken.None)
            .ConfigureAwait(false);

        // Act
        await using (var lease = await dataSource
                         .OpenConnectionAsync(CancellationToken.None)
                         .ConfigureAwait(false))
        {
            using var cancellation = new CancellationTokenSource();
            var pending = cache.GetAsync("external-pool", cancellation.Token);

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
                    .ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(
                        TimeSpan.FromSeconds(15),
                        CancellationToken.None))
                    .ConfigureAwait(false);
            }
        }

        // Assert
        Assert.Equal(
            new byte[] { 4, 2 },
            await cache
                .GetAsync("external-pool", CancellationToken.None)
                .ConfigureAwait(false));
    }

    private static async Task AssertExternalDataSourceSurvivesProviderDisposalAsync(
        CacheIntegrationStore store
    )
    {
        // Arrange
        await using var dataSource = CreateExternalDataSource(store);
        await using var provider = CreateExternalCacheProvider(store, dataSource);
        var cache = provider.GetRequiredService<IBufferDistributedCache>();
        await cache
            .SetAsync("external-disposal", [4, 2], new DistributedCacheEntryOptions(), CancellationToken.None)
            .ConfigureAwait(false);

        // Act
        await provider
            .DisposeAsync()
            .ConfigureAwait(false);

        // Assert
        await Assert
            .ThrowsAsync<ObjectDisposedException>(() => cache.GetAsync("external-disposal", CancellationToken.None))
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

    private static MySqlDataSource CreateExternalDataSource(
        CacheIntegrationStore store,
        Action? onPasswordRequest = null,
        Action? onConnectionOpen = null
    )
    {
        var connectionBuilder = new MySqlConnectionStringBuilder(store.ConnectionString)
        {
            AutoEnlist = false,
            MaximumPoolSize = 1,
        };

        var password = connectionBuilder.Password;
        connectionBuilder.Remove("Password");
        var dataSourceBuilder = new MySqlDataSourceBuilder(connectionBuilder.ConnectionString);

        // WHY: A supplied data source must retain its caller-owned password and open callbacks.
        dataSourceBuilder.UsePeriodicPasswordProvider(
            (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                onPasswordRequest?.Invoke();

                return ValueTask.FromResult(password);
            },
            TimeSpan.FromHours(1),
            TimeSpan.FromSeconds(1));

        dataSourceBuilder.UseConnectionOpenedCallback((_, token) =>
        {
            token.ThrowIfCancellationRequested();
            onConnectionOpen?.Invoke();

            return ValueTask.CompletedTask;
        });

        return dataSourceBuilder.Build();
    }

    private static ServiceProvider CreateExternalCacheProvider(
        CacheIntegrationStore store,
        MySqlDataSource dataSource
    )
    {
        var services = new ServiceCollection();
        services.AddDistributedMySqlCache(options =>
        {
            options.DataSource = dataSource;
            options.SchemaName = store.SchemaName;
            options.TableName = store.TableName;
        });

        return services.BuildServiceProvider();
    }

    private static async Task AssertCleanupIntervalAndBatchLimitAsync(
        CacheIntegrationStore store
    )
    {
        await store
            .InsertExpiredEntriesAsync(1005)
            .ConfigureAwait(false);

        var time = new CacheManualTimeProvider();
        var logger = new CacheRecordingLogger();
        await using var cache = store.CreateCache(time, logger);

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
    }

    private static async Task AssertCanceledCleanupAndContinuationAsync(
        CacheIntegrationStore store,
        string initiator
    )
    {
        // Arrange
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
        var result = await AssertCleanupCancellationAsync(store, cache, time, initiator)
            .ConfigureAwait(false);

        // Assert
        Assert.Empty(logger.Entries);

        Assert.Equal(
            new byte[] { 1 },
            await cache
                .GetAsync("live", CancellationToken.None)
                .ConfigureAwait(false));

        Assert.Equal(
            2L,
            await store
                .CountExpiredAsync()
                .ConfigureAwait(false));

        await AssertCleanupInitiatorResultAsync(cache, initiator, result)
            .ConfigureAwait(false);

        time.Advance(TimeSpan.FromMinutes(5));

        // WHY: Recovery is verified with a small blocked batch; the separate bound test covers full batches.
        // ReSharper disable once MethodHasAsyncOverload
        Assert.Null(cache.Get("cleanup-miss"));

        Assert.Equal(
            0L,
            await store
                .CountExpiredAsync()
                .ConfigureAwait(false));
    }

    private static async Task<byte[]?> AssertCleanupCancellationAsync(
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

    private static async Task AssertCleanupInitiatorResultAsync(
        MySqlDistributedCache cache,
        string initiator,
        byte[]? result
    )
    {
        switch (initiator)
        {
            case "get":
            case "try-get":
                Assert.Equal(new byte[] { 1 }, result);
                break;

            case "set-array":
                Assert.Equal(
                    new byte[] { 2 },
                    await cache
                        .GetAsync("cleanup-array", CancellationToken.None)
                        .ConfigureAwait(false));
                break;

            case "set-sequence":
                Assert.Equal(
                    new byte[] { 3 },
                    await cache
                        .GetAsync("cleanup-sequence", CancellationToken.None)
                        .ConfigureAwait(false));
                break;

            case "set-segmented":
                Assert.Equal(
                    new byte[] { 3, 4 },
                    await cache
                        .GetAsync("cleanup-segmented", CancellationToken.None)
                        .ConfigureAwait(false));
                break;

            case "refresh":
                Assert.Null(result);
                break;

            case "remove":
                Assert.Null(
                    await cache
                        .GetAsync("cleanup-array", CancellationToken.None)
                        .ConfigureAwait(false));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(initiator));
        }
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

/// <summary>
/// Shares one deployed cache table per selected engine across independent contract cases.
/// </summary>
public sealed class MySqlDistributedCacheFixture : IAsyncLifetime
{
    private readonly Dictionary<IntegrationDatabaseTarget, CacheIntegrationStore> _stores = new();
    private readonly Dictionary<IntegrationDatabaseTarget, CacheIntegrationStore> _expirationStores = new();

    /// <summary>Defers database setup until a selected target is actually exercised.</summary>
    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>Disposes the target-owned tables after the test class has completed.</summary>
    public async Task DisposeAsync()
    {
        List<Exception>? failures = null;

        foreach (var store in _stores.Values.Concat(_expirationStores.Values))
        {
            try
            {
                await store
                    .DisposeAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                // WHY: One failed database teardown must not strand the other engine fixtures.
                (failures ??= new List<Exception>()).Add(failure);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException("Cache fixture cleanup failed.", failures);
        }
    }

    /// <summary>Returns the one deployed store owned by this class for the target.</summary>
    internal Task<CacheIntegrationStore> GetStoreAsync(
        IntegrationDatabaseTarget target
    ) => GetStoreAsync(target, _stores);

    /// <summary>Returns the isolated expiration table for the target.</summary>
    internal Task<CacheIntegrationStore> GetExpirationStoreAsync(
        IntegrationDatabaseTarget target
    ) => GetStoreAsync(target, _expirationStores);

    private static async Task<CacheIntegrationStore> GetStoreAsync(
        IntegrationDatabaseTarget target,
        Dictionary<IntegrationDatabaseTarget, CacheIntegrationStore> stores
    )
    {
        // WHY: xUnit serializes tests in this class, so fixture setup needs no extra lock.
        if (stores.TryGetValue(target, out var existing))
        {
            return existing;
        }

        // WHY: Sharing schema setup avoids a CREATE/DROP cycle for every named contract test.
        // Ordinary and expiration cases use separate tables so expired rows cannot affect other contracts.
        var store = await CacheIntegrationStore
            .CreateAsync(target)
            .ConfigureAwait(false);

        try
        {
            await store
                .ExecuteAsync(MySqlCacheSchema.GetCreateScript(store.SchemaName, store.TableName))
                .ConfigureAwait(false);

            stores.Add(target, store);

            return store;
        }
        catch
        {
            await store
                .DisposeAsync()
                .ConfigureAwait(false);

            throw;
        }
    }
}
