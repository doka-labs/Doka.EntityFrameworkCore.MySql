using System.Buffers;
using System.Text.Json.Nodes;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Tests for the structural JSON comparer contracts, snapshot isolation, concurrent use,
/// and the pooled fallback buffer used by document cloning and customized JSON values.
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

    /// <summary>JsonElement object equality follows JSON semantics rather than property order.</summary>
    [Fact]
    public void JsonElement_reordered_properties_are_equal_and_have_equal_hashes()
    {
        using var docA = JsonDocument.Parse("""{"a":1,"b":{"c":2,"d":3}}""");
        using var docB = JsonDocument.Parse("""{"b":{"d":3,"c":2},"a":1}""");

        Assert.True(MySqlJsonValueComparers.JsonElementComparer.Equals(docA.RootElement, docB.RootElement));
        Assert.Equal(
            MySqlJsonValueComparers.JsonElementComparer.GetHashCode(docA.RootElement),
            MySqlJsonValueComparers.JsonElementComparer.GetHashCode(docB.RootElement));
    }

    /// <summary>Equivalent JSON number spellings remain equal and hash-compatible.</summary>
    [Fact]
    public void JsonElement_equivalent_number_representations_are_equal_and_have_equal_hashes()
    {
        using var docA = JsonDocument.Parse("""{"value":10e-3}""");
        using var docB = JsonDocument.Parse("""{"value":0.01}""");

        Assert.True(MySqlJsonValueComparers.JsonElementComparer.Equals(docA.RootElement, docB.RootElement));
        Assert.Equal(
            MySqlJsonValueComparers.JsonElementComparer.GetHashCode(docA.RootElement),
            MySqlJsonValueComparers.JsonElementComparer.GetHashCode(docB.RootElement));
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

    [Fact]
    public void JsonDocument_snapshot_does_not_borrow_returned_pool_memory()
    {
        var pool = new TrackingArrayPool();
        using var source = JsonDocument.Parse("""{"a":1,"b":[1,2,3]}""");
        using var snapshot = MySqlJsonValueComparers.CloneJsonDocument(source, pool)!;
        var reusedBuffer = pool.Rent(1);

        try
        {
            Array.Fill(reusedBuffer, (byte)' ');

            Assert.Equal(1, snapshot.RootElement.GetProperty("a").GetInt32());
            Assert.Equal(3, snapshot.RootElement.GetProperty("b").GetArrayLength());
        }
        finally
        {
            pool.Return(reusedBuffer);
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

    [Fact]
    public void JsonNode_semantically_equal_values_are_symmetric_and_hash_compatible()
    {
        var guid = Guid.Parse("ebde3793-3e08-4a89-aa39-76b92c1c4c72");
        var equalPairs = new (JsonNode Left, JsonNode Right)[]
        {
            (
                JsonNode.Parse("""{"a":1,"nested":{"b":2,"c":3}}""")!,
                JsonNode.Parse("""{"nested":{"c":3,"b":2},"a":1}""")!),
            (JsonNode.Parse("10e-3")!, JsonNode.Parse("0.01")!),
            (
                JsonNode.Parse("""{"number":1,"text":"value"}""")!,
                new JsonObject { ["number"] = 1L, ["text"] = "value" }),
            (JsonValue.Create(1)!, JsonValue.Create(1L)!),
            (JsonNode.Parse("\"\\u0076alue\"")!, JsonValue.Create("value")!),
            (JsonValue.Create(guid)!, JsonValue.Create(guid.ToString())!),
        };

        foreach (var (left, right) in equalPairs)
        {
            Assert.True(MySqlJsonValueComparers.JsonNodeComparer.Equals(left, right));
            Assert.True(MySqlJsonValueComparers.JsonNodeComparer.Equals(right, left));
            Assert.Equal(
                MySqlJsonValueComparers.JsonNodeComparer.GetHashCode(left),
                MySqlJsonValueComparers.JsonNodeComparer.GetHashCode(right));
        }
    }

    [Fact]
    public void JsonNode_semantically_distinct_values_are_symmetric()
    {
        var distinctPairs = new (JsonNode Left, JsonNode Right)[]
        {
            (JsonNode.Parse("[1,2]")!, JsonNode.Parse("[2,1]")!),
            (JsonNode.Parse("""{"value":null}""")!, JsonNode.Parse("{}")!),
            (JsonValue.Create(1)!, JsonValue.Create("1")!),
            (JsonNode.Parse("""{"nested":{"value":1}}""")!, JsonNode.Parse("""{"nested":{"value":2}}""")!),
        };

        foreach (var (left, right) in distinctPairs)
        {
            Assert.False(MySqlJsonValueComparers.JsonNodeComparer.Equals(left, right));
            Assert.False(MySqlJsonValueComparers.JsonNodeComparer.Equals(right, left));
        }
    }

    [Fact]
    public void JsonNode_case_insensitive_objects_remain_symmetric_and_hash_compatible()
    {
        var options = new JsonNodeOptions { PropertyNameCaseInsensitive = true };
        var left = new JsonObject(options) { ["Name"] = 1 };
        var right = new JsonObject(options) { ["name"] = 1L };

        Assert.True(MySqlJsonValueComparers.JsonNodeComparer.Equals(left, right));
        Assert.True(MySqlJsonValueComparers.JsonNodeComparer.Equals(right, left));
        Assert.Equal(
            MySqlJsonValueComparers.JsonNodeComparer.GetHashCode(left),
            MySqlJsonValueComparers.JsonNodeComparer.GetHashCode(right));
    }

    [Fact]
    public void JsonNode_objects_with_different_name_comparison_options_remain_symmetric()
    {
        var sensitive = new JsonObject { ["Name"] = 1 };
        var insensitive = new JsonObject(
            new JsonNodeOptions { PropertyNameCaseInsensitive = true })
        {
            ["name"] = 1L,
        };

        Assert.False(MySqlJsonValueComparers.JsonNodeComparer.Equals(sensitive, insensitive));
        Assert.False(MySqlJsonValueComparers.JsonNodeComparer.Equals(insensitive, sensitive));
    }

    [Fact]
    public void JsonNode_nested_objects_with_different_name_comparison_options_remain_symmetric()
    {
        var sensitive = new JsonArray(
            new JsonObject { ["Name"] = 1 });
        var insensitive = new JsonArray(
            new JsonObject(new JsonNodeOptions { PropertyNameCaseInsensitive = true })
            {
                ["name"] = 1L,
            });

        Assert.False(MySqlJsonValueComparers.JsonNodeComparer.Equals(sensitive, insensitive));
        Assert.False(MySqlJsonValueComparers.JsonNodeComparer.Equals(insensitive, sensitive));
    }

    [Fact]
    public void JsonNode_reparented_object_uses_materialized_dictionary_comparer_symmetrically()
    {
        var insensitive = new JsonNodeOptions { PropertyNameCaseInsensitive = true };
        var materializedSensitiveChild = new JsonObject { ["Name"] = 1 };
        var left = new JsonObject(insensitive) { ["child"] = materializedSensitiveChild };
        var right = new JsonObject(insensitive)
        {
            ["child"] = new JsonObject(insensitive) { ["name"] = 1L },
        };
        var expected = JsonNode.DeepEquals(left, right) && JsonNode.DeepEquals(right, left);

        Assert.False(expected);
        Assert.Equal(expected, MySqlJsonValueComparers.JsonNodeComparer.Equals(left, right));
        Assert.Equal(expected, MySqlJsonValueComparers.JsonNodeComparer.Equals(right, left));
    }

    [Fact]
    public void JsonNode_one_pass_equality_matches_bidirectional_deep_equals()
    {
        var insensitive = new JsonNodeOptions { PropertyNameCaseInsensitive = true };
        var pairs = new (JsonNode? Left, JsonNode? Right)[]
        {
            (
                JsonNode.Parse("""{"a":1,"nested":{"b":2,"c":3}}"""),
                JsonNode.Parse("""{"nested":{"c":3,"b":2},"a":1}""")),
            (JsonNode.Parse("""{"value":1}"""), JsonNode.Parse("""{"value":2}""")),
            (
                new JsonObject { ["Name"] = 1 },
                new JsonObject(insensitive) { ["Name"] = 1L }),
            (
                new JsonObject { ["Name"] = 1 },
                new JsonObject(insensitive) { ["name"] = 1L }),
            (
                new JsonObject(insensitive) { ["Name"] = 1 },
                new JsonObject(insensitive) { ["name"] = 1L }),
            (
                new JsonArray(new JsonObject { ["Name"] = 1 }),
                new JsonArray(new JsonObject(insensitive) { ["name"] = 1L })),
            (
                new JsonObject { ["A"] = 1, ["a"] = 2 },
                new JsonObject(insensitive) { ["A"] = 1, ["B"] = 2 }),
            (JsonNode.Parse("[1,2,3]"), JsonNode.Parse("[1,3,2]")),
            (JsonNode.Parse("10e-3"), JsonNode.Parse("0.01")),
            (JsonValue.Create(1), JsonValue.Create(1L)),
            (
                JsonValue.Create(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }),
                JsonNode.Parse("""{"b":2,"a":1}""")),
            (
                JsonValue.Create(new List<int> { 1, 2, 3 }),
                JsonNode.Parse("[1,2,3]")),
            (JsonNode.Parse("true"), null),
            (null, null),
        };

        foreach (var (left, right) in pairs)
        {
            var expected = JsonNode.DeepEquals(left, right) && JsonNode.DeepEquals(right, left);

            Assert.Equal(expected, MySqlJsonValueComparers.JsonNodeComparer.Equals(left, right));
            Assert.Equal(expected, MySqlJsonValueComparers.JsonNodeComparer.Equals(right, left));
        }
    }

    [Fact]
    public void JsonNode_primitive_equality_and_hashing_match_bidirectional_deep_equals_for_supported_types()
    {
        var guid = Guid.Parse("ebde3793-3e08-4a89-aa39-76b92c1c4c72");
        var timestamp = new DateTime(2026, 8, 22, 12, 34, 56, DateTimeKind.Utc);
        JsonNode[] values =
        [
            JsonNode.Parse("1")!,
            JsonNode.Parse("1.0")!,
            JsonValue.Create((byte)1)!,
            JsonValue.Create((sbyte)1)!,
            JsonValue.Create((short)1)!,
            JsonValue.Create((ushort)1)!,
            JsonValue.Create(1)!,
            JsonValue.Create(1U)!,
            JsonValue.Create(1L)!,
            JsonValue.Create(1UL)!,
            JsonValue.Create((Half)1)!,
            JsonValue.Create(1F)!,
            JsonValue.Create(1D)!,
            JsonValue.Create(1M)!,
            JsonValue.Create((Int128)1)!,
            JsonValue.Create((UInt128)1)!,
            JsonNode.Parse("true")!,
            JsonValue.Create(true)!,
            JsonNode.Parse("\"value\"")!,
            JsonValue.Create("value")!,
            JsonNode.Parse("\"v\"")!,
            JsonValue.Create('v')!,
            JsonNode.Parse($"\"{guid}\"")!,
            JsonValue.Create(guid)!,
            JsonNode.Parse($"\"{timestamp:O}\"")!,
            JsonValue.Create(timestamp)!,
        ];

        foreach (var left in values)
        {
            foreach (var right in values)
            {
                var expected = JsonNode.DeepEquals(left, right)
                    && JsonNode.DeepEquals(right, left);

                Assert.Equal(expected, MySqlJsonValueComparers.JsonNodeComparer.Equals(left, right));
                Assert.Equal(expected, MySqlJsonValueComparers.JsonNodeComparer.Equals(right, left));

                if (expected)
                {
                    Assert.Equal(
                        MySqlJsonValueComparers.JsonNodeComparer.GetHashCode(left),
                        MySqlJsonValueComparers.JsonNodeComparer.GetHashCode(right));
                }
            }
        }
    }

    [Fact]
    public void JsonNode_container_edges_match_bidirectional_deep_equals()
    {
        var pairs = new (JsonNode Left, JsonNode Right)[]
        {
            (new JsonArray { null }, new JsonArray { null }),
            (new JsonArray { null }, new JsonArray { 1 }),
            (JsonNode.Parse("{}")!, JsonNode.Parse("[]")!),
            (JsonNode.Parse("{}")!, JsonNode.Parse("true")!),
            (JsonNode.Parse("[]")!, JsonNode.Parse("{}")!),
            (JsonNode.Parse("[]")!, JsonNode.Parse("true")!),
            (JsonNode.Parse("[1]")!, JsonNode.Parse("[1,2]")!),
        };

        foreach (var (left, right) in pairs)
        {
            var expected = JsonNode.DeepEquals(left, right)
                && JsonNode.DeepEquals(right, left);

            Assert.Equal(expected, MySqlJsonValueComparers.JsonNodeComparer.Equals(left, right));
            Assert.Equal(expected, MySqlJsonValueComparers.JsonNodeComparer.Equals(right, left));

            if (expected)
            {
                Assert.Equal(
                    MySqlJsonValueComparers.JsonNodeComparer.GetHashCode(left),
                    MySqlJsonValueComparers.JsonNodeComparer.GetHashCode(right));
            }
        }
    }

    [Theory]
    [InlineData("1e100")]
    [InlineData("1e9999")]
    public void JsonElement_extreme_numbers_remain_equal_and_hash_compatible(
        string json
    )
    {
        using var first = JsonDocument.Parse(json);
        using var second = JsonDocument.Parse(json);

        Assert.True(MySqlJsonValueComparers.JsonElementComparer.Equals(first.RootElement, second.RootElement));
        Assert.Equal(
            MySqlJsonValueComparers.JsonElementComparer.GetHashCode(first.RootElement),
            MySqlJsonValueComparers.JsonElementComparer.GetHashCode(second.RootElement));
    }

    [Fact]
    public void JsonNode_one_pass_equality_does_not_allocate_per_iteration()
    {
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

        for (var index = 0; index < 1000; index++)
        {
            result &= comparer.Equals(left, right);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 1000; index++)
        {
            result &= comparer.Equals(left, right);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(result);

        Assert.True(result);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void JsonNode_numeric_values_contribute_to_hash_distribution()
    {
        var hashes = Enumerable
            .Range(1, 5)
            .Select(index => JsonNode.Parse($$$"""{"id":{{{index}}},"name":"item"}""")!)
            .Select(MySqlJsonValueComparers.JsonNodeComparer.GetHashCode)
            .Distinct()
            .Count();

        Assert.True(hashes >= 4);
    }

    [Fact]
    public void JsonNode_customized_numeric_value_is_hash_compatible_with_builtin_number()
    {
        var customized = JsonValue.Create(CustomNumber.One)!;
        var builtin = JsonValue.Create(1)!;

        AssertJsonNodesEqualAndHashCompatible(customized, builtin);
    }

    [Fact]
    public void JsonNode_object_hashing_does_not_allocate_per_iteration()
    {
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

        for (var index = 0; index < 1000; index++)
        {
            hash ^= comparer.GetHashCode(node);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 1000; index++)
        {
            hash ^= comparer.GetHashCode(node);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(hash);

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void JsonNode_customized_object_and_array_values_are_hash_compatible()
    {
        var customizedObject = JsonValue.Create(
            new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 })!;
        var parsedObject = JsonNode.Parse("""{"b":2,"a":1}""")!;
        var customizedArray = JsonValue.Create(new List<int> { 1, 2, 3 })!;
        var parsedArray = JsonNode.Parse("[1,2,3]")!;

        AssertJsonNodesEqualAndHashCompatible(customizedObject, parsedObject);
        AssertJsonNodesEqualAndHashCompatible(customizedArray, parsedArray);
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
    public void JsonNode_snapshot_is_deeply_independent()
    {
        var source = JsonNode.Parse("""{"nested":{"value":1},"items":[1,2]}""")!;
        var snapshot = MySqlJsonValueComparers.JsonNodeComparer.Snapshot(source)!;

        snapshot["nested"]!["value"] = 2;
        snapshot["items"]!.AsArray().Add(3);

        Assert.Equal(1, source["nested"]!["value"]!.GetValue<int>());
        Assert.Equal(2, source["items"]!.AsArray().Count);
        Assert.False(MySqlJsonValueComparers.JsonNodeComparer.Equals(source, snapshot));
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

        var firstNode = JsonNode.Parse(json)!;
        var secondNode = JsonNode.Parse(json)!;
        var expectedNodeHash = MySqlJsonValueComparers.JsonNodeComparer.GetHashCode(firstNode);

        Parallel.For(
            0,
            1000,
            _ =>
            {
                Assert.True(MySqlJsonValueComparers.JsonNodeComparer.Equals(firstNode, secondNode));
                Assert.Equal(expectedNodeHash, MySqlJsonValueComparers.JsonNodeComparer.GetHashCode(secondNode));
            });
    }

    private sealed class TrackingArrayPool : ArrayPool<byte>
    {
        private readonly List<byte[]> _available = [];
        private readonly List<byte[]> _outstanding = [];

        public int RentCount { get; private set; }

        public int ReturnCount { get; private set; }

        public int OutstandingCount => _outstanding.Count;

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

    private static void AssertJsonNodesEqualAndHashCompatible(
        JsonNode left,
        JsonNode right
    )
    {
        Assert.True(MySqlJsonValueComparers.JsonNodeComparer.Equals(left, right));
        Assert.True(MySqlJsonValueComparers.JsonNodeComparer.Equals(right, left));
        Assert.Equal(
            MySqlJsonValueComparers.JsonNodeComparer.GetHashCode(left),
            MySqlJsonValueComparers.JsonNodeComparer.GetHashCode(right));
    }

    private enum CustomNumber
    {
        One = 1,
    }
}
