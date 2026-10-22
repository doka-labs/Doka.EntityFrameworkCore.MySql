using System.Buffers;
using Microsoft.Extensions.Caching.Distributed;

namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlCacheRegistrationTests
{
    /// <summary>
    /// Supplies every cache operation for both synchronous and asynchronous disposal.
    /// </summary>
    public static TheoryData<string, bool> DisposedOperationCases => new()
    {
        { "Get", false },
        { "Get", true },
        { "SetBytes", false },
        { "SetBytes", true },
        { "SetBuffer", false },
        { "SetBuffer", true },
        { "TryGet", false },
        { "TryGet", true },
        { "Refresh", false },
        { "Refresh", true },
        { "Remove", false },
        { "Remove", true },
    };

    /// <summary>
    /// Verifies alias disposal is idempotent and cannot reopen a disposed cache owner.
    /// </summary>
    [Fact]
    public void Disposing_shared_aliases_is_idempotent_and_rejects_further_database_access()
    {
        // Arrange
        using var provider = MySqlCacheTestFactory.CreateProvider();
        var cache = provider.GetRequiredService<IDistributedCache>();
        var bufferCache = provider.GetRequiredService<IBufferDistributedCache>();

        // Act
        // WHY: Disposing both synchronous aliases must be safe even though they share one owner.
        // ReSharper disable once MethodHasAsyncOverload
        Assert.IsAssignableFrom<IDisposable>(cache).Dispose();
        // WHY: The second alias must exercise the same synchronous disposal contract.
        // ReSharper disable once MethodHasAsyncOverload
        Assert.IsAssignableFrom<IDisposable>(bufferCache).Dispose();

        // WHY: This assertion protects the synchronous API after both aliases have been disposed.
        // ReSharper disable once MethodHasAsyncOverload
        var exception = Record.Exception(() => cache.Get("key"));

        // Assert
        Assert.IsType<ObjectDisposedException>(exception);
    }

    /// <summary>
    /// Verifies each synchronous operation rejects a disposed owner before using its borrowed pool.
    /// </summary>
    /// <param name="operation">The cache entry point to invoke.</param>
    /// <param name="asynchronousDisposal">Whether disposal uses the asynchronous contract.</param>
    [Theory]
    [MemberData(nameof(DisposedOperationCases))]
    public async Task Disposed_cache_rejects_synchronous_operations(
        string operation,
        bool asynchronousDisposal
    )
    {
        // Arrange
        await using var dataSource = CreateBorrowedDataSource();
        await using var provider = CreateBorrowingProvider(dataSource);
        var cache = provider.GetRequiredService<IBufferDistributedCache>();
        var invoke = CreateSynchronousOperation(cache, operation);
        await DisposeCacheAsync(cache, asynchronousDisposal);

        // Act
        var exception = Record.Exception(invoke);

        // Assert
        Assert.IsType<ObjectDisposedException>(exception);
    }

    /// <summary>
    /// Verifies each asynchronous operation rejects a disposed owner before using its borrowed pool.
    /// </summary>
    /// <param name="operation">The cache entry point to invoke.</param>
    /// <param name="asynchronousDisposal">Whether disposal uses the asynchronous contract.</param>
    [Theory]
    [MemberData(nameof(DisposedOperationCases))]
    public async Task Disposed_cache_rejects_asynchronous_operations(
        string operation,
        bool asynchronousDisposal
    )
    {
        // Arrange
        await using var dataSource = CreateBorrowedDataSource();
        await using var provider = CreateBorrowingProvider(dataSource);
        var cache = provider.GetRequiredService<IBufferDistributedCache>();
        var invoke = CreateAsynchronousOperation(cache, operation);
        await DisposeCacheAsync(cache, asynchronousDisposal);

        // Act
        var exception = await Record.ExceptionAsync(invoke);

        // Assert
        Assert.IsType<ObjectDisposedException>(exception);
    }

    /// <summary>
    /// Verifies disposing the cache and its service provider preserves the caller-owned data source.
    /// </summary>
    /// <param name="asynchronousDisposal">Whether cache disposal uses the asynchronous contract.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disposal_preserves_a_borrowed_data_source(
        bool asynchronousDisposal
    )
    {
        // Arrange
        await using var dataSource = CreateBorrowedDataSource();
        await using var provider = CreateBorrowingProvider(dataSource);
        var cache = provider.GetRequiredService<IBufferDistributedCache>();

        // Act
        await DisposeCacheAsync(cache, asynchronousDisposal);
        await provider.DisposeAsync();
        await using var connection = dataSource.CreateConnection();

        // Assert
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    private static MySqlDataSource CreateBorrowedDataSource() => new(
        "Server=127.0.0.1;Port=1;User ID=cache;AutoEnlist=false;Pooling=false");

    private static ServiceProvider CreateBorrowingProvider(
        MySqlDataSource dataSource
    ) => MySqlCacheTestFactory.CreateProvider(options =>
    {
        options.ConnectionString = string.Empty;
        options.DataSource = dataSource;
    });

    private static async ValueTask DisposeCacheAsync(
        IBufferDistributedCache cache,
        bool asynchronousDisposal
    )
    {
        if (asynchronousDisposal)
        {
            await Assert.IsAssignableFrom<IAsyncDisposable>(cache).DisposeAsync();
        }
        else
        {
            // WHY: Disposal mode is a test dimension; substituting DisposeAsync would omit its synchronous half.
            // ReSharper disable once MethodHasAsyncOverload
            Assert.IsAssignableFrom<IDisposable>(cache).Dispose();
        }
    }

    private static Action CreateSynchronousOperation(
        IBufferDistributedCache cache,
        string operation
    ) => operation switch
    {
        // WHY: Each delegate must exercise the synchronous API's disposed-owner guard directly.
        // ReSharper disable once MethodHasAsyncOverload
        "Get" => () => cache.Get("key"),
        // WHY: The byte-array overload has its own disposed-owner guard.
        // ReSharper disable once MethodHasAsyncOverload
        "SetBytes" => () => cache.Set("key", new byte[] { 1 }, new DistributedCacheEntryOptions()),
        // WHY: The buffer overload must reject a disposed owner independently of the byte-array overload.
        // ReSharper disable once MethodHasAsyncOverload
        "SetBuffer" => () => cache.Set("key", ReadOnlySequence<byte>.Empty, new DistributedCacheEntryOptions()),
        // WHY: This delegate covers the synchronous buffer-read contract.
        // ReSharper disable once MethodHasAsyncOverload
        "TryGet" => () => cache.TryGet("key", new ArrayBufferWriter<byte>()),
        // WHY: This delegate covers the synchronous refresh contract.
        // ReSharper disable once MethodHasAsyncOverload
        "Refresh" => () => cache.Refresh("key"),
        // WHY: This delegate covers the synchronous removal contract.
        // ReSharper disable once MethodHasAsyncOverload
        "Remove" => () => cache.Remove("key"),
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown cache operation."),
    };

    private static Func<Task> CreateAsynchronousOperation(
        IBufferDistributedCache cache,
        string operation
    ) => operation switch
    {
        "Get" => () => cache.GetAsync("key", CancellationToken.None),
        "SetBytes" => () => cache.SetAsync(
            "key", new byte[] { 1 }, new DistributedCacheEntryOptions(), CancellationToken.None),
        "SetBuffer" => () => cache.SetAsync(
            "key", ReadOnlySequence<byte>.Empty, new DistributedCacheEntryOptions(), CancellationToken.None).AsTask(),
        "TryGet" => () => cache.TryGetAsync("key", new ArrayBufferWriter<byte>(), CancellationToken.None).AsTask(),
        "Refresh" => () => cache.RefreshAsync("key", CancellationToken.None),
        "Remove" => () => cache.RemoveAsync("key", CancellationToken.None),
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown cache operation."),
    };
}
