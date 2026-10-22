using System.Buffers;
using System.Net;
using Microsoft.Extensions.Caching.Distributed;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Covers cache input rejection and cancellation before any network or destination access.
/// </summary>
public sealed class MySqlCacheInputTests
{
    /// <summary>Returns every invalid key and operation as an independently reported case.</summary>
    public static IEnumerable<object[]> InvalidKeyCases
    {
        get
        {
            string[] operations = ["get", "set_array", "refresh", "remove", "get_buffer", "set_sequence", "set_segments"];
            string[] scenarios =
            [
                "null", "empty", "oversized_ascii", "oversized_utf8",
                "unpaired_high_surrogate", "unpaired_low_surrogate",
            ];

            foreach (var operation in operations)
            {
                foreach (var scenario in scenarios)
                {
                    yield return [operation, scenario];
                }
            }
        }
    }

    /// <summary>
    /// Verifies one synchronous operation rejects an invalid key before opening a connection.
    /// </summary>
    /// <param name="operation">The cache API and payload path under test.</param>
    /// <param name="scenario">The invalid key representation.</param>
    [Theory]
    [MemberData(nameof(InvalidKeyCases))]
    public void Synchronous_operations_reject_invalid_keys_before_network_access(
        string operation,
        string scenario
    )
    {
        // Arrange
        using var provider = MySqlCacheTestFactory.CreateProvider();
        var cache = provider.GetRequiredService<IDistributedCache>();
        var bufferCache = provider.GetRequiredService<IBufferDistributedCache>();
        var key = CreateInvalidKey(scenario);
        var options = new DistributedCacheEntryOptions();
        var destination = new UntouchedBufferWriter();
        byte[] value = [1, 2, 3];

        // WHY: each case targets the synchronous entry point's own validation.
        Action action = operation switch
        {
            // ReSharper disable once MethodHasAsyncOverload
            "get" => () => cache.Get(key),
            // ReSharper disable once MethodHasAsyncOverload
            "set_array" => () => cache.Set(key, value, options),
            // ReSharper disable once MethodHasAsyncOverload
            "refresh" => () => cache.Refresh(key),
            // ReSharper disable once MethodHasAsyncOverload
            "remove" => () => cache.Remove(key),
            // ReSharper disable once MethodHasAsyncOverload
            "get_buffer" => () => bufferCache.TryGet(key, destination),
            // ReSharper disable once MethodHasAsyncOverload
            "set_sequence" => () => bufferCache.Set(key, new ReadOnlySequence<byte>(value), options),
            // ReSharper disable once MethodHasAsyncOverload
            "set_segments" => () => bufferCache.Set(key, CreateMultiSegmentValue(), options),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        // Act
        var exception = Record.Exception(action);

        // Assert
        Assert.IsAssignableFrom<ArgumentException>(exception);
    }

    /// <summary>
    /// Verifies one asynchronous operation rejects an invalid key before opening a connection.
    /// </summary>
    /// <param name="operation">The cache API and payload path under test.</param>
    /// <param name="scenario">The invalid key representation.</param>
    [Theory]
    [MemberData(nameof(InvalidKeyCases))]
    public async Task Asynchronous_operations_reject_invalid_keys_before_network_access(
        string operation,
        string scenario
    )
    {
        // Arrange
        using var provider = MySqlCacheTestFactory.CreateProvider();
        var cache = provider.GetRequiredService<IDistributedCache>();
        var bufferCache = provider.GetRequiredService<IBufferDistributedCache>();
        var key = CreateInvalidKey(scenario);
        var options = new DistributedCacheEntryOptions();
        var destination = new UntouchedBufferWriter();
        byte[] value = [1, 2, 3];

        Func<Task> action = operation switch
        {
            "get" => () => cache.GetAsync(key, CancellationToken.None),
            "set_array" => () => cache.SetAsync(key, value, options, CancellationToken.None),
            "refresh" => () => cache.RefreshAsync(key, CancellationToken.None),
            "remove" => () => cache.RemoveAsync(key, CancellationToken.None),
            "get_buffer" => () => bufferCache.TryGetAsync(key, destination, CancellationToken.None).AsTask(),
            "set_sequence" => () => bufferCache
                .SetAsync(key, new ReadOnlySequence<byte>(value), options, CancellationToken.None).AsTask(),
            "set_segments" => () => bufferCache
                .SetAsync(key, CreateMultiSegmentValue(), options, CancellationToken.None).AsTask(),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        // Act
        var exception = await Record.ExceptionAsync(action);

        // Assert
        Assert.IsAssignableFrom<ArgumentException>(exception);
    }

    /// <summary>
    /// Verifies one missing synchronous argument is rejected before connecting.
    /// </summary>
    /// <param name="scenario">The cache API and missing argument under test.</param>
    /// <param name="parameterName">The required argument named by the exception.</param>
    [Theory]
    [InlineData("array_value", "value")]
    [InlineData("array_options", "options")]
    [InlineData("buffer_destination", "destination")]
    [InlineData("sequence_options", "options")]
    public void Synchronous_null_argument_is_rejected_before_network_access(
        string scenario,
        string parameterName
    )
    {
        // Arrange
        using var provider = MySqlCacheTestFactory.CreateProvider();
        var cache = provider.GetRequiredService<IDistributedCache>();
        var bufferCache = provider.GetRequiredService<IBufferDistributedCache>();
        var options = new DistributedCacheEntryOptions();

        // WHY: synchronous argument validation is a separate public entry point.
        Action action = scenario switch
        {
            // ReSharper disable once MethodHasAsyncOverload
            "array_value" => () => cache.Set("key", null!, options),
            // ReSharper disable once MethodHasAsyncOverload
            "array_options" => () => cache.Set("key", [], null!),
            // ReSharper disable once MethodHasAsyncOverload
            "buffer_destination" => () => bufferCache.TryGet("key", null!),
            // ReSharper disable once MethodHasAsyncOverload
            "sequence_options" => () => bufferCache.Set("key", ReadOnlySequence<byte>.Empty, null!),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        // Act
        var exception = Record.Exception(action);

        // Assert
        Assert.Equal(parameterName, Assert.IsType<ArgumentNullException>(exception).ParamName);
    }

    /// <summary>Verifies one missing asynchronous argument is rejected before connecting.</summary>
    /// <param name="scenario">The cache API and missing argument under test.</param>
    /// <param name="parameterName">The required argument named by the exception.</param>
    [Theory]
    [InlineData("array_value", "value")]
    [InlineData("array_options", "options")]
    [InlineData("buffer_destination", "destination")]
    [InlineData("sequence_options", "options")]
    public async Task Asynchronous_null_argument_is_rejected_before_network_access(
        string scenario,
        string parameterName
    )
    {
        // Arrange
        using var provider = MySqlCacheTestFactory.CreateProvider();
        var cache = provider.GetRequiredService<IDistributedCache>();
        var bufferCache = provider.GetRequiredService<IBufferDistributedCache>();
        var options = new DistributedCacheEntryOptions();

        Func<Task> action = scenario switch
        {
            "array_value" => () => cache.SetAsync("key", null!, options, CancellationToken.None),
            "array_options" => () => cache.SetAsync("key", [], null!, CancellationToken.None),
            "buffer_destination" => () => bufferCache.TryGetAsync("key", null!, CancellationToken.None).AsTask(),
            "sequence_options" => () => bufferCache
                .SetAsync("key", ReadOnlySequence<byte>.Empty, null!, CancellationToken.None).AsTask(),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        // Act
        var exception = await Record.ExceptionAsync(action);

        // Assert
        Assert.Equal(parameterName, Assert.IsType<ArgumentNullException>(exception).ParamName);
    }

    /// <summary>
    /// Verifies one synchronous write path rejects unsupported expiration precision.
    /// </summary>
    /// <param name="path">The array, single-segment or multi-segment write path.</param>
    [Theory]
    [InlineData("array")]
    [InlineData("sequence")]
    [InlineData("segments")]
    public void Synchronous_invalid_expiration_is_rejected_before_network_access(
        string path
    )
    {
        // Arrange
        using var provider = MySqlCacheTestFactory.CreateProvider();
        var cache = provider.GetRequiredService<IDistributedCache>();
        var bufferCache = provider.GetRequiredService<IBufferDistributedCache>();
        var options = new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromTicks(1) };

        // WHY: every synchronous payload path must validate before opening its connection.
        Action action = path switch
        {
            // ReSharper disable once MethodHasAsyncOverload
            "array" => () => cache.Set("key", [], options),
            // ReSharper disable once MethodHasAsyncOverload
            "sequence" => () => bufferCache.Set("key", ReadOnlySequence<byte>.Empty, options),
            // ReSharper disable once MethodHasAsyncOverload
            "segments" => () => bufferCache.Set("key", CreateMultiSegmentValue(), options),
            _ => throw new ArgumentOutOfRangeException(nameof(path)),
        };

        // Act
        var exception = Record.Exception(action);

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    /// <summary>Verifies one asynchronous write path rejects unsupported expiration precision.</summary>
    /// <param name="path">The array, single-segment or multi-segment write path.</param>
    [Theory]
    [InlineData("array")]
    [InlineData("sequence")]
    [InlineData("segments")]
    public async Task Asynchronous_invalid_expiration_is_rejected_before_network_access(
        string path
    )
    {
        // Arrange
        using var provider = MySqlCacheTestFactory.CreateProvider();
        var cache = provider.GetRequiredService<IDistributedCache>();
        var bufferCache = provider.GetRequiredService<IBufferDistributedCache>();
        var options = new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromTicks(1) };

        Func<Task> action = path switch
        {
            "array" => () => cache.SetAsync("key", [], options, CancellationToken.None),
            "sequence" => () => bufferCache
                .SetAsync("key", ReadOnlySequence<byte>.Empty, options, CancellationToken.None).AsTask(),
            "segments" => () => bufferCache
                .SetAsync("key", CreateMultiSegmentValue(), options, CancellationToken.None).AsTask(),
            _ => throw new ArgumentOutOfRangeException(nameof(path)),
        };

        // Act
        var exception = await Record.ExceptionAsync(action);

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    /// <summary>
    /// Verifies canceled operations neither connect nor write to their destination.
    /// </summary>
    /// <param name="operation">The cache API and payload path under test.</param>
    [Theory]
    [InlineData("get")]
    [InlineData("set_array")]
    [InlineData("refresh")]
    [InlineData("remove")]
    [InlineData("get_buffer")]
    [InlineData("set_sequence")]
    [InlineData("set_segments")]
    public async Task Precanceled_operations_do_not_connect_or_touch_the_destination(
        string operation
    )
    {
        // Arrange
        using var listener = new TcpListener(IPAddress.Loopback, 0);

        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var provider = MySqlCacheTestFactory.CreateProvider(options =>
            options.ConnectionString = $"Server=127.0.0.1;Port={port};User ID=cache;Pooling=false");

        var cache = provider.GetRequiredService<IDistributedCache>();
        var bufferCache = provider.GetRequiredService<IBufferDistributedCache>();
        var options = new DistributedCacheEntryOptions();
        var token = new CancellationToken(true);
        var destination = new UntouchedBufferWriter();

        Func<Task> action = operation switch
        {
            "get" => () => cache.GetAsync("key", token),
            "set_array" => () => cache.SetAsync("key", [1, 2], options, token),
            "refresh" => () => cache.RefreshAsync("key", token),
            "remove" => () => cache.RemoveAsync("key", token),
            "get_buffer" => () => bufferCache.TryGetAsync("key", destination, token).AsTask(),
            "set_sequence" => () => bufferCache.SetAsync("key", ReadOnlySequence<byte>.Empty, options, token).AsTask(),
            "set_segments" => () => bufferCache.SetAsync("key", CreateMultiSegmentValue(), options, token).AsTask(),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        // Act
        var exception = await Record.ExceptionAsync(action);

        // Assert
        Assert.Equal(token, Assert.IsAssignableFrom<OperationCanceledException>(exception).CancellationToken);
        Assert.False(listener.Pending());
    }

    /// <summary>
    /// Verifies key limits count UTF-8 bytes and permit binary-distinct key content.
    /// </summary>
    /// <param name="scenario">The valid boundary or binary-distinct key representation.</param>
    [Theory]
    [InlineData("ascii_limit")]
    [InlineData("utf8_limit")]
    [InlineData("supplementary_limit")]
    [InlineData("spaces")]
    [InlineData("embedded_null")]
    public async Task Valid_keys_reach_cancellation_instead_of_input_rejection(
        string scenario
    )
    {
        // Arrange
        var key = scenario switch
        {
            "ascii_limit" => new string('a', 1024),
            "utf8_limit" => new string('\u00e9', 512),
            "supplementary_limit" => string.Concat(Enumerable.Repeat("\ud83d\ude00", 256)),
            "spaces" => " ",
            "embedded_null" => "prefix\0suffix",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        using var provider = MySqlCacheTestFactory.CreateProvider();
        var cache = provider.GetRequiredService<IDistributedCache>();
        var token = new CancellationToken(true);

        // Act
        var exception = await Record.ExceptionAsync(() => cache.GetAsync(key, token));

        // Assert
        Assert.Equal(token, Assert.IsAssignableFrom<OperationCanceledException>(exception).CancellationToken);
    }

    private static string CreateInvalidKey(
        string scenario
    ) => scenario switch
    {
        "null" => null!,
        "empty" => string.Empty,
        "oversized_ascii" => new string('a', 1025),
        "oversized_utf8" => new string('\u00e9', 513),
        "unpaired_high_surrogate" => "prefix\ud800",
        "unpaired_low_surrogate" => "prefix\udfff",
        _ => throw new ArgumentOutOfRangeException(nameof(scenario))
    };

    private static ReadOnlySequence<byte> CreateMultiSegmentValue()
    {
        var first = new BufferSegment(new byte[] { 1, 2 });
        var last = first.Append(new byte[] { 3, 4 });

        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private sealed class BufferSegment : ReadOnlySequenceSegment<byte>
    {
        public BufferSegment(
            ReadOnlyMemory<byte> memory
        )
        {
            Memory = memory;
        }

        public BufferSegment Append(
            ReadOnlyMemory<byte> memory
        )
        {
            var next = new BufferSegment(memory) { RunningIndex = RunningIndex + Memory.Length };

            Next = next;

            return next;
        }
    }

    // WHY: validation and precancellation must complete before requesting any destination memory.
    private sealed class UntouchedBufferWriter : IBufferWriter<byte>
    {
        public void Advance(
            int count
        ) => throw new InvalidOperationException("The destination must remain untouched.");

        public Memory<byte> GetMemory(
            int sizeHint = 0
        ) => throw new InvalidOperationException("The destination must remain untouched.");

        public Span<byte> GetSpan(
            int sizeHint = 0
        ) => throw new InvalidOperationException("The destination must remain untouched.");
    }
}
