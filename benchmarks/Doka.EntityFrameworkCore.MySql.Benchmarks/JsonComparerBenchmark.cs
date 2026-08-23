namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

/// <summary>
/// Measures the change-tracking hot path for JSON-shaped properties. The previous
/// implementation produced a fresh string per equals comparison and per hash; the
/// optimized equality path performs one symmetric structural traversal. The benchmark
/// retains both the serialized implementation and the bidirectional BCL traversal as
/// explicit regression references. Run with MemoryDiagnoser to surface the allocation
/// drop and per-comparison throughput against a representative payload size.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class JsonComparerBenchmark
{
    private const int PayloadCount = 1000;
    private const int FreshPayloadCount = 10;

    private readonly string _payload;
    private readonly JsonDocument _documentA;
    private readonly JsonDocument _documentB;
    private readonly JsonNode _nodeA;
    private readonly JsonNode _nodeB;

    public JsonComparerBenchmark()
    {
        _payload = BuildPayload();
        _documentA = JsonDocument.Parse(_payload);
        _documentB = JsonDocument.Parse(_payload);
        _nodeA = JsonNode.Parse(_payload)!;
        _nodeB = JsonNode.Parse(_payload)!;
    }

    /// <summary>
    /// Naive reference for JSON deep-equality: per-call <see cref="JsonElement.GetRawText"/>
    /// allocation plus string-compare. The fast-path under test
    /// (<see cref="JsonElementEqualsLoop"/>) walks the existing DOM through
    /// <see cref="JsonElement.DeepEquals"/>; BDN reports Ratio = Mean[fast]/Mean[naive]
    /// and Allocated-ratio. The DoD gate is alloc-reduction >= 80%
    /// (Allocated ratio &lt;= 0.2).
    /// </summary>
    [Benchmark(Baseline = true)]
    public bool NaiveJsonElementEqualsLoop()
    {
        var result = true;

        for (var i = 0; i < PayloadCount; i++)
        {
            var leftText = _documentA.RootElement.GetRawText();
            var rightText = _documentB.RootElement.GetRawText();
            result &= string.Equals(leftText, rightText, StringComparison.Ordinal);
        }

        return result;
    }

    [Benchmark]
    public bool JsonElementEqualsLoop()
    {
        var comparer = MySqlJsonValueComparers.JsonElementComparer;
        var result = true;

        for (var i = 0; i < PayloadCount; i++)
        {
            result &= comparer.Equals(_documentA.RootElement, _documentB.RootElement);
        }

        return result;
    }

    [Benchmark]
    public int JsonElementHashLoop()
    {
        var comparer = MySqlJsonValueComparers.JsonElementComparer;
        var hash = 0;

        for (var i = 0; i < PayloadCount; i++)
        {
            hash ^= comparer.GetHashCode(_documentA.RootElement);
        }

        return hash;
    }

    [Benchmark]
    public bool JsonNodeEqualsLoop()
    {
        var comparer = MySqlJsonValueComparers.JsonNodeComparer;
        var result = true;

        for (var i = 0; i < PayloadCount; i++)
        {
            result &= comparer.Equals(_nodeA, _nodeB);
        }

        return result;
    }

    [Benchmark]
    public bool BidirectionalJsonNodeDeepEqualsLoop()
    {
        var result = true;

        for (var i = 0; i < PayloadCount; i++)
        {
            result &= JsonNode.DeepEquals(_nodeA, _nodeB)
                && JsonNode.DeepEquals(_nodeB, _nodeA);
        }

        return result;
    }

    [Benchmark]
    public int JsonNodeHashLoop()
    {
        var comparer = MySqlJsonValueComparers.JsonNodeComparer;
        var hash = 0;

        for (var i = 0; i < PayloadCount; i++)
        {
            hash ^= comparer.GetHashCode(_nodeA);
        }

        return hash;
    }

    [Benchmark]
    public bool SerializedJsonNodeEqualsLoop()
    {
        var result = true;

        for (var i = 0; i < PayloadCount; i++)
        {
            result &= string.Equals(
                _nodeA.ToJsonString(),
                _nodeB.ToJsonString(),
                StringComparison.Ordinal);
        }

        return result;
    }

    [Benchmark]
    public int SerializedJsonNodeHashLoop()
    {
        var hash = 0;

        for (var i = 0; i < PayloadCount; i++)
        {
            hash ^= _nodeA.ToJsonString().GetHashCode(StringComparison.Ordinal);
        }

        return hash;
    }

    [Benchmark]
    public JsonNode FreshJsonNodeParseLoop()
    {
        JsonNode? node = null;
        for (var index = 0; index < FreshPayloadCount; index++)
        {
            node = JsonNode.Parse(_payload);
        }

        return node!;
    }

    [Benchmark]
    public bool FreshJsonNodeParseAndEqualsLoop()
    {
        var result = true;
        for (var index = 0; index < FreshPayloadCount; index++)
        {
            var left = JsonNode.Parse(_payload)!;
            var right = JsonNode.Parse(_payload)!;
            result &= MySqlJsonValueComparers.JsonNodeComparer.Equals(left, right);
        }

        return result;
    }

    [Benchmark]
    public JsonNode? JsonNodeSnapshotLoop()
    {
        var comparer = MySqlJsonValueComparers.JsonNodeComparer;
        JsonNode? clone = null;

        for (var i = 0; i < PayloadCount; i++)
        {
            clone = comparer.Snapshot(_nodeA);
        }

        return clone;
    }

    private static string BuildPayload()
    {
        var builder = new StringBuilder(8 * 1024);
        builder.Append("{\"items\":[");

        for (var i = 0; i < 50; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder
                .Append("{\"id\":")
                .Append(i)
                .Append(",\"name\":\"item-")
                .Append(i)
                .Append("\",\"tags\":[\"alpha\",\"beta\",\"gamma\"],\"metadata\":{\"score\":")
                .Append(i * 10)
                .Append(",\"active\":true,\"created\":\"2026-05-16T01:23:45Z\"}}");
        }

        builder.Append("]}");

        return builder.ToString();
    }
}
