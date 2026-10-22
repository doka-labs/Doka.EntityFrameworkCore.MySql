namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Verifies byte-array and buffer entry contracts.</summary>
public sealed partial class MySqlDistributedCacheIntegrationTests
{
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
}
