using System.Buffers;
using System.Net;
using Microsoft.Extensions.Caching.Distributed;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Covers cache input rejection and cancellation before any network or destination access.
/// </summary>
public sealed class MySqlCacheInputTests
{
    /// <summary>
    /// Verifies every synchronous operation rejects invalid keys before opening a connection.
    /// </summary>
    [Theory]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("oversized_ascii")]
    [InlineData("oversized_utf8")]
    [InlineData("unpaired_high_surrogate")]
    [InlineData("unpaired_low_surrogate")]
    public void Synchronous_operations_reject_invalid_keys_before_network_access(
        string scenario
    )
    {
        using var provider = MySqlCacheTestFactory.CreateProvider();
        var cache = provider.GetRequiredService<IDistributedCache>();
        var bufferCache = provider.GetRequiredService<IBufferDistributedCache>();
        var key = CreateInvalidKey(scenario);
        var options = new DistributedCacheEntryOptions();
        var destination = new UntouchedBufferWriter();
        byte[] value = [1, 2, 3];

        Assert.ThrowsAny<ArgumentException>(() => cache.Get(key));
        Assert.ThrowsAny<ArgumentException>(() => cache.Set(key, value, options));
        Assert.ThrowsAny<ArgumentException>(() => cache.Refresh(key));
        Assert.ThrowsAny<ArgumentException>(() => cache.Remove(key));
        Assert.ThrowsAny<ArgumentException>(() => bufferCache.TryGet(key, destination));
        Assert.ThrowsAny<ArgumentException>(() => bufferCache.Set(key, new ReadOnlySequence<byte>(value), options));
        Assert.ThrowsAny<ArgumentException>(() => bufferCache.Set(key, CreateMultiSegmentValue(), options));
    }

    /// <summary>
    /// Verifies every asynchronous operation rejects invalid keys before opening a connection.
    /// </summary>
    [Theory]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("oversized_ascii")]
    [InlineData("oversized_utf8")]
    [InlineData("unpaired_high_surrogate")]
    [InlineData("unpaired_low_surrogate")]
    public async Task Asynchronous_operations_reject_invalid_keys_before_network_access(
        string scenario
    )
    {
        using var provider = MySqlCacheTestFactory.CreateProvider();
        var cache = provider.GetRequiredService<IDistributedCache>();
        var bufferCache = provider.GetRequiredService<IBufferDistributedCache>();
        var key = CreateInvalidKey(scenario);
        var options = new DistributedCacheEntryOptions();
        var destination = new UntouchedBufferWriter();
        byte[] value = [1, 2, 3];

        await Assert.ThrowsAnyAsync<ArgumentException>(() => cache.GetAsync(key, CancellationToken.None));
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            cache.SetAsync(key, value, options, CancellationToken.None));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => cache.RefreshAsync(key, CancellationToken.None));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => cache.RemoveAsync(key, CancellationToken.None));
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            bufferCache
                .TryGetAsync(key, destination, CancellationToken.None)
                .AsTask());
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            bufferCache
                .SetAsync(key, new ReadOnlySequence<byte>(value), options, CancellationToken.None)
                .AsTask());
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            bufferCache
                .SetAsync(key, CreateMultiSegmentValue(), options, CancellationToken.None)
                .AsTask());
    }

    /// <summary>
    /// Verifies missing values, entry options, and buffer destinations are rejected immediately.
    /// </summary>
    [Fact]
    public async Task Null_values_options_and_destinations_are_rejected_before_network_access()
    {
        using var provider = MySqlCacheTestFactory.CreateProvider();
        var cache = provider.GetRequiredService<IDistributedCache>();
        var bufferCache = provider.GetRequiredService<IBufferDistributedCache>();
        var options = new DistributedCacheEntryOptions();

        Assert.Throws<ArgumentNullException>("value", () => cache.Set("key", null!, options));
        Assert.Throws<ArgumentNullException>("options", () => cache.Set("key", [], null!));
        Assert.Throws<ArgumentNullException>("destination", () => bufferCache.TryGet("key", null!));
        Assert.Throws<ArgumentNullException>("options", () =>
            bufferCache.Set("key", ReadOnlySequence<byte>.Empty, null!));

        await Assert.ThrowsAsync<ArgumentNullException>("value", () =>
            cache.SetAsync("key", null!, options, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>("options", () =>
            cache.SetAsync("key", [], null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>("destination", () =>
            bufferCache
                .TryGetAsync("key", null!, CancellationToken.None)
                .AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>("options", () =>
            bufferCache
                .SetAsync("key", ReadOnlySequence<byte>.Empty, null!, CancellationToken.None)
                .AsTask());
    }

    /// <summary>
    /// Verifies invalid expiration is rejected by every write path before connection creation.
    /// </summary>
    [Fact]
    public async Task Invalid_expiration_is_rejected_by_array_and_sequence_write_paths()
    {
        using var provider = MySqlCacheTestFactory.CreateProvider();
        var cache = provider.GetRequiredService<IDistributedCache>();
        var bufferCache = provider.GetRequiredService<IBufferDistributedCache>();
        var options = new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromTicks(1) };

        Assert.Throws<ArgumentOutOfRangeException>(() => cache.Set("key", [], options));
        Assert.Throws<ArgumentOutOfRangeException>(() => bufferCache.Set("key", ReadOnlySequence<byte>.Empty, options));
        Assert.Throws<ArgumentOutOfRangeException>(() => bufferCache.Set("key", CreateMultiSegmentValue(), options));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            cache.SetAsync("key", [], options, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            bufferCache
                .SetAsync("key", ReadOnlySequence<byte>.Empty, options, CancellationToken.None)
                .AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            bufferCache
                .SetAsync("key", CreateMultiSegmentValue(), options, CancellationToken.None)
                .AsTask());
    }

    /// <summary>
    /// Verifies canceled operations neither connect nor write to their destination.
    /// </summary>
    [Fact]
    public async Task Precanceled_operations_do_not_connect_or_touch_the_destination()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var provider = MySqlCacheTestFactory.CreateProvider(options =>
            options.ConnectionString = $"Server=127.0.0.1;Port={port};User ID=cache;Pooling=false");
        var cache = provider.GetRequiredService<IDistributedCache>();
        var bufferCache = provider.GetRequiredService<IBufferDistributedCache>();
        var options = new DistributedCacheEntryOptions();
        var token = new CancellationToken(true);

        await AssertCanceledAsync(() => cache.GetAsync("key", token), token);
        await AssertCanceledAsync(() => cache.SetAsync("key", [1, 2], options, token), token);
        await AssertCanceledAsync(() => cache.RefreshAsync("key", token), token);
        await AssertCanceledAsync(() => cache.RemoveAsync("key", token), token);
        await AssertCanceledAsync(() =>
            bufferCache.TryGetAsync("key", new UntouchedBufferWriter(), token).AsTask(), token);
        await AssertCanceledAsync(() =>
            bufferCache.SetAsync("key", ReadOnlySequence<byte>.Empty, options, token).AsTask(), token);
        await AssertCanceledAsync(() =>
            bufferCache.SetAsync("key", CreateMultiSegmentValue(), options, token).AsTask(), token);

        Assert.False(listener.Pending());
    }

    /// <summary>
    /// Verifies key limits count UTF-8 bytes and permit binary-distinct key content.
    /// </summary>
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

        await AssertCanceledAsync(() => cache.GetAsync(key, token), token);
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

    private static async Task AssertCanceledAsync(
        Func<Task> action,
        CancellationToken token
    )
    {
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(action);
        Assert.Equal(token, exception.CancellationToken);
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

    private sealed class UntouchedBufferWriter : IBufferWriter<byte>
    {
        public void Advance(int count) => throw new InvalidOperationException("The destination must remain untouched.");
        public Memory<byte> GetMemory(int sizeHint = 0) =>
            throw new InvalidOperationException("The destination must remain untouched.");
        public Span<byte> GetSpan(int sizeHint = 0) =>
            throw new InvalidOperationException("The destination must remain untouched.");
    }
}
