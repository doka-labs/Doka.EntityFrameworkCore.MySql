using System.Buffers;
using System.Text.Json.Nodes;

namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlJsonValueComparersTests
{
    /// <summary>
    /// Verifies repeated one-pass node equality allocates no memory after warmup.
    /// </summary>
    [Fact]
    public void JsonNode_one_pass_equality_does_not_allocate_per_iteration()
    {
        // Arrange
        var json = "["
            + string.Join(
                ",",
                Enumerable
                    .Range(0, 100)
                    .Select(index => $"{{\"id\":{index},\"value\":\"item-{index:D6}\"}}"))
            + "]";

        var left = JsonNode.Parse(json)!;
        var right = JsonNode.Parse(json)!;
        var comparer = MySqlJsonValueComparers.JsonNodeComparer;
        var result = comparer.Equals(left, right);

        // WHY: Warm up the comparer before measuring so initialization is outside the steady-state contract.
        for (var index = 0; index < 1000; index++)
        {
            result &= comparer.Equals(left, right);
        }

        // Act
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 1000; index++)
        {
            result &= comparer.Equals(left, right);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(result);

        // Assert
        Assert.True(result);
        Assert.Equal(0, allocated);
    }

    /// <summary>
    /// Verifies repeated object hashing allocates no memory after warmup.
    /// </summary>
    [Fact]
    public void JsonNode_object_hashing_does_not_allocate_per_iteration()
    {
        // Arrange
        var json = "["
            + string.Join(
                ",",
                Enumerable
                    .Range(0, 100)
                    .Select(index => $"{{\"id\":{index},\"value\":\"item-{index:D6}\"}}"))
            + "]";

        var node = JsonNode.Parse(json)!;
        var comparer = MySqlJsonValueComparers.JsonNodeComparer;
        var hash = comparer.GetHashCode(node);

        // WHY: Warmup excludes one-time runtime initialization from the steady-state allocation assertion.
        for (var index = 0; index < 1000; index++)
        {
            hash ^= comparer.GetHashCode(node);
        }

        // Act
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 1000; index++)
        {
            hash ^= comparer.GetHashCode(node);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(hash);

        // Assert
        Assert.Equal(0, allocated);
    }

    /// <summary>
    /// Verifies growth and repeated disposal return every rented buffer exactly once.
    /// </summary>
    [Fact]
    public void Pooled_stream_returns_every_rented_buffer_exactly_once_after_growth()
    {
        // Arrange
        var pool = new TrackingArrayPool();
        var stream = new MySqlJsonValueComparers.PooledByteBufferStream(pool, initialCapacity: 8);

        // Act
        stream.Write(new byte[4096]);
        stream.Dispose();
        stream.Dispose();

        // Assert
        Assert.Equal(pool.RentCount, pool.ReturnCount);
        Assert.Equal(2, pool.RentCount);
        Assert.Equal(0, pool.OutstandingCount);
    }

    /// <summary>
    /// Verifies an exceptional writer still returns every rented buffer.
    /// </summary>
    [Fact]
    public void Pooled_stream_releases_current_buffer_when_caller_fails()
    {
        // Arrange
        var pool = new TrackingArrayPool();

        // Act
        var exception = Record.Exception(FailAfterGrowth);

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(pool.RentCount, pool.ReturnCount);
        Assert.Equal(0, pool.OutstandingCount);

        return;

        void FailAfterGrowth()
        {
            using var stream = new MySqlJsonValueComparers.PooledByteBufferStream(pool, initialCapacity: 8);
            stream.Write(new byte[4096]);
            throw new InvalidOperationException("Simulated writer failure.");
        }
    }

    /// <summary>
    /// Verifies document comparison and hashing can share large inputs across concurrent readers.
    /// </summary>
    [Fact]
    public void JsonElement_comparison_and_hashing_are_thread_safe_for_large_payloads()
    {
        // Arrange
        var json = CreateLargeJsonPayload();
        using var first = JsonDocument.Parse(json);
        using var second = JsonDocument.Parse(json);
        var expectedHash = MySqlJsonValueComparers.JsonElementComparer.GetHashCode(first.RootElement);
        var results = new (bool Equal, int Hash)[1000];

        // Act
        Parallel.For(
            0,
            results.Length,
            index =>
            {
                results[index] = (
                    MySqlJsonValueComparers.JsonElementComparer.Equals(first.RootElement, second.RootElement),
                    MySqlJsonValueComparers.JsonElementComparer.GetHashCode(second.RootElement));
            });

        // Assert
        Assert.All(results, result =>
        {
            Assert.True(result.Equal);
            Assert.Equal(expectedHash, result.Hash);
        });
    }

    /// <summary>
    /// Verifies node comparison and hashing can share large inputs across concurrent readers.
    /// </summary>
    [Fact]
    public void JsonNode_comparison_and_hashing_are_thread_safe_for_large_payloads()
    {
        // Arrange
        var json = CreateLargeJsonPayload();
        var first = JsonNode.Parse(json)!;
        var second = JsonNode.Parse(json)!;
        var expectedHash = MySqlJsonValueComparers.JsonNodeComparer.GetHashCode(first);
        var results = new (bool Equal, int Hash)[1000];

        // Act
        Parallel.For(
            0,
            results.Length,
            index =>
            {
                results[index] = (
                    MySqlJsonValueComparers.JsonNodeComparer.Equals(first, second),
                    MySqlJsonValueComparers.JsonNodeComparer.GetHashCode(second));
            });

        // Assert
        Assert.All(results, result =>
        {
            Assert.True(result.Equal);
            Assert.Equal(expectedHash, result.Hash);
        });
    }

    private static string CreateLargeJsonPayload() => "["
        + string.Join(
            ",",
            Enumerable
                .Range(0, 500)
                .Select(index => $"{{\"id\":{index},\"value\":\"item-{index:D6}\"}}"))
        + "]";

    /// <summary>
    /// Tracks buffer ownership and reuses returned arrays to expose borrowing or double-return regressions.
    /// </summary>
    private sealed class TrackingArrayPool : ArrayPool<byte>
    {
        private readonly List<byte[]> _available = [];
        private readonly List<byte[]> _outstanding = [];

        /// <summary>Gets the number of rented buffers.</summary>
        public int RentCount { get; private set; }

        /// <summary>Gets the number of returned buffers.</summary>
        public int ReturnCount { get; private set; }

        /// <summary>Gets the number of buffers still owned by callers.</summary>
        public int OutstandingCount => _outstanding.Count;

        /// <inheritdoc />
        public override byte[] Rent(
            int minimumLength
        )
        {
            var availableIndex = _available.FindIndex(buffer => buffer.Length >= minimumLength);
            var buffer = availableIndex < 0
                ? new byte[Math.Max(minimumLength, 1)]
                : _available[availableIndex];

            if (availableIndex >= 0)
            {
                _available.RemoveAt(availableIndex);
            }

            _outstanding.Add(buffer);
            RentCount++;

            return buffer;
        }

        /// <inheritdoc />
        public override void Return(
            byte[] array,
            bool clearArray = false
        )
        {
            var index = _outstanding.FindIndex(candidate => ReferenceEquals(candidate, array));

            Assert.True(index >= 0, "A buffer was returned more than once or did not originate from this pool.");
            _outstanding.RemoveAt(index);
            _available.Add(array);
            ReturnCount++;
        }
    }
}
