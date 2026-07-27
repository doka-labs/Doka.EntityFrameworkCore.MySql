using System.Buffers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Tests for the JSON value comparers: covers reference-equality short-circuits, null-argument
/// paths, length-mismatch fast-fail, and the buffer-grow path that fires when a serialized
/// document exceeds the initial 1 KB rented buffer.
/// </summary>
public sealed class MySqlJsonValueComparersTests
{
    // -- JsonElementComparer --

    /// <summary>JsonElement equals on two equivalent documents.</summary>
    [Fact]
    public void JsonElement_equal_documents_are_equal()
    {
        using var docA = JsonDocument.Parse("""{"a":1,"b":2}""");
        using var docB = JsonDocument.Parse("""{"a":1,"b":2}""");
        Assert.True(MySqlJsonValueComparers.JsonElementComparer.Equals(docA.RootElement, docB.RootElement));
    }

    /// <summary>JsonElement equals fails fast when canonical lengths differ.</summary>
    [Fact]
    public void JsonElement_different_length_documents_are_not_equal()
    {
        using var docA = JsonDocument.Parse("""{"a":1}""");
        using var docB = JsonDocument.Parse("""{"a":1,"b":2}""");
        Assert.False(MySqlJsonValueComparers.JsonElementComparer.Equals(docA.RootElement, docB.RootElement));
    }

    /// <summary>JsonElement equals on two same-length but different documents.</summary>
    [Fact]
    public void JsonElement_same_length_different_content_are_not_equal()
    {
        using var docA = JsonDocument.Parse("""{"a":1}""");
        using var docB = JsonDocument.Parse("""{"a":2}""");
        Assert.False(MySqlJsonValueComparers.JsonElementComparer.Equals(docA.RootElement, docB.RootElement));
    }

    /// <summary>JsonElement equals on a payload larger than the initial 1 KB buffer triggers the grow path.</summary>
    [Fact]
    public void JsonElement_large_payload_triggers_buffer_grow()
    {
        // Each entry is roughly 24 bytes; 200 entries push past the 1024 initial buffer.
        var large = "[" + string.Join(",", Enumerable.Range(0, 200).Select(i => $"\"item-{i:D6}\"")) + "]";
        using var docA = JsonDocument.Parse(large);
        using var docB = JsonDocument.Parse(large);
        Assert.True(MySqlJsonValueComparers.JsonElementComparer.Equals(docA.RootElement, docB.RootElement));
    }

    /// <summary>JsonElement hash is stable across two parses of the same JSON.</summary>
    [Fact]
    public void JsonElement_hash_is_stable_across_equal_documents()
    {
        using var docA = JsonDocument.Parse("""{"a":1,"b":"x"}""");
        using var docB = JsonDocument.Parse("""{"a":1,"b":"x"}""");
        Assert.Equal(
            MySqlJsonValueComparers.JsonElementComparer.GetHashCode(docA.RootElement),
            MySqlJsonValueComparers.JsonElementComparer.GetHashCode(docB.RootElement));
    }

    // -- JsonDocumentComparer --

    /// <summary>Same-reference JsonDocument is equal to itself.</summary>
    [Fact]
    public void JsonDocument_reference_equal_is_equal()
    {
        using var doc = JsonDocument.Parse("""{"a":1}""");
        Assert.True(MySqlJsonValueComparers.JsonDocumentComparer.Equals(doc, doc));
    }

    /// <summary>JsonDocument equals returns false when one side is null.</summary>
    [Fact]
    public void JsonDocument_one_null_side_is_not_equal()
    {
        using var doc = JsonDocument.Parse("""{"a":1}""");
        Assert.False(MySqlJsonValueComparers.JsonDocumentComparer.Equals(doc, null));
        Assert.False(MySqlJsonValueComparers.JsonDocumentComparer.Equals(null, doc));
    }

    /// <summary>JsonDocument equals returns true when both sides are null.</summary>
    [Fact]
    public void JsonDocument_both_null_is_equal() =>
        Assert.True(MySqlJsonValueComparers.JsonDocumentComparer.Equals(null, null));

    /// <summary>JsonDocument hash for null returns zero.</summary>
    [Fact]
    public void JsonDocument_hash_for_null_is_zero() =>
        Assert.Equal(0, MySqlJsonValueComparers.JsonDocumentComparer.GetHashCode(null!));

    /// <summary>Snapshot of null JsonDocument returns null.</summary>
    [Fact]
    public void JsonDocument_snapshot_of_null_is_null() =>
        Assert.Null(MySqlJsonValueComparers.JsonDocumentComparer.Snapshot(null));

    /// <summary>Snapshot of JsonDocument produces an equal but independent document.</summary>
    [Fact]
    public void JsonDocument_snapshot_round_trips()
    {
        using var source = JsonDocument.Parse("""{"a":1,"b":[1,2,3]}""");
        var snapshot = MySqlJsonValueComparers.JsonDocumentComparer.Snapshot(source);
        try
        {
            Assert.NotSame(source, snapshot);
            Assert.True(MySqlJsonValueComparers.JsonDocumentComparer.Equals(source, snapshot));
        }
        finally
        {
            snapshot?.Dispose();
        }
    }

    // -- JsonNodeComparer --

    /// <summary>Same-reference JsonNode is equal to itself.</summary>
    [Fact]
    public void JsonNode_reference_equal_is_equal()
    {
        var node = JsonNode.Parse("""{"a":1}""");
        Assert.True(MySqlJsonValueComparers.JsonNodeComparer.Equals(node, node));
    }

    /// <summary>JsonNode equals returns false when one side is null.</summary>
    [Fact]
    public void JsonNode_one_null_side_is_not_equal()
    {
        var node = JsonNode.Parse("""{"a":1}""");
        Assert.False(MySqlJsonValueComparers.JsonNodeComparer.Equals(node, null));
        Assert.False(MySqlJsonValueComparers.JsonNodeComparer.Equals(null, node));
    }

    /// <summary>JsonNode equals returns true when both sides are null.</summary>
    [Fact]
    public void JsonNode_both_null_is_equal() =>
        Assert.True(MySqlJsonValueComparers.JsonNodeComparer.Equals(null, null));

    /// <summary>JsonNode equals on two parses of the same JSON.</summary>
    [Fact]
    public void JsonNode_equal_payloads_are_equal()
    {
        var a = JsonNode.Parse("""{"a":1,"b":2}""");
        var b = JsonNode.Parse("""{"a":1,"b":2}""");
        Assert.True(MySqlJsonValueComparers.JsonNodeComparer.Equals(a, b));
    }

    /// <summary>JsonNode equals fails fast when serialized lengths differ.</summary>
    [Fact]
    public void JsonNode_different_length_payloads_are_not_equal()
    {
        var a = JsonNode.Parse("""{"a":1}""");
        var b = JsonNode.Parse("""{"a":1,"b":2}""");
        Assert.False(MySqlJsonValueComparers.JsonNodeComparer.Equals(a, b));
    }

    /// <summary>JsonNode hash for null returns zero.</summary>
    [Fact]
    public void JsonNode_hash_for_null_is_zero() =>
        Assert.Equal(0, MySqlJsonValueComparers.JsonNodeComparer.GetHashCode(null!));

    /// <summary>Snapshot of null JsonNode returns null.</summary>
    [Fact]
    public void JsonNode_snapshot_of_null_is_null() =>
        Assert.Null(MySqlJsonValueComparers.JsonNodeComparer.Snapshot(null));

    /// <summary>Snapshot of JsonNode produces an equal but independent node.</summary>
    [Fact]
    public void JsonNode_snapshot_round_trips()
    {
        var source = JsonNode.Parse("""{"a":1,"b":[1,2,3]}""");
        var snapshot = MySqlJsonValueComparers.JsonNodeComparer.Snapshot(source);
        Assert.NotSame(source, snapshot);
        Assert.True(MySqlJsonValueComparers.JsonNodeComparer.Equals(source, snapshot));
    }

    [Fact]
    public void Pooled_stream_returns_every_rented_buffer_exactly_once_after_growth()
    {
        var pool = new TrackingArrayPool();
        var stream = new MySqlJsonValueComparers.PooledByteBufferStream(pool, initialCapacity: 8);

        stream.Write(new byte[4096]);
        stream.Dispose();
        stream.Dispose();

        Assert.Equal(pool.RentCount, pool.ReturnCount);
        Assert.Equal(2, pool.RentCount);
        Assert.Equal(0, pool.OutstandingCount);
    }

    [Fact]
    public void Pooled_stream_releases_current_buffer_when_caller_fails()
    {
        var pool = new TrackingArrayPool();

        Assert.Throws<InvalidOperationException>((Action)FailAfterGrowth);

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

    [Fact]
    public void Json_comparison_and_hashing_are_thread_safe_for_large_payloads()
    {
        var json = "["
            + string.Join(
                ",",
                Enumerable
                    .Range(0, 500)
                    .Select(index => $"{{\"id\":{index},\"value\":\"item-{index:D6}\"}}"))
            + "]";
        using var first = JsonDocument.Parse(json);
        using var second = JsonDocument.Parse(json);
        var expectedHash = MySqlJsonValueComparers.JsonElementComparer.GetHashCode(first.RootElement);

        Parallel.For(
            0,
            1000,
            _ =>
            {
                Assert.True(MySqlJsonValueComparers.JsonElementComparer.Equals(first.RootElement, second.RootElement));
                Assert.Equal(expectedHash, MySqlJsonValueComparers.JsonElementComparer.GetHashCode(second.RootElement));
            });
    }

    private sealed class TrackingArrayPool : ArrayPool<byte>
    {
        private readonly List<byte[]> _outstanding = [];

        public int RentCount { get; private set; }

        public int ReturnCount { get; private set; }

        public int OutstandingCount => _outstanding.Count;

        public override byte[] Rent(
            int minimumLength
        )
        {
            var buffer = new byte[Math.Max(minimumLength, 1)];
            _outstanding.Add(buffer);
            RentCount++;
            return buffer;
        }

        public override void Return(
            byte[] array,
            bool clearArray = false
        )
        {
            var index = _outstanding.FindIndex(candidate => ReferenceEquals(candidate, array));
            Assert.True(index >= 0, "A buffer was returned more than once or did not originate from this pool.");
            _outstanding.RemoveAt(index);
            ReturnCount++;
        }
    }
}
