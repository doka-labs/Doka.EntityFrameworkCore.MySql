namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Verifies contiguous and segmented sequence writes.</summary>
public sealed partial class MySqlDistributedCacheIntegrationTests
{
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
}
