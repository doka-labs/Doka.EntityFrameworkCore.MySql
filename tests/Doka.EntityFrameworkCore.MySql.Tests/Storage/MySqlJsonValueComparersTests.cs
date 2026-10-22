namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies structural JSON equality, snapshot isolation, concurrent use, and pooled buffer ownership.
/// </summary>
public sealed partial class MySqlJsonValueComparersTests
{
    /// <summary>JsonElement equals on two equivalent documents.</summary>
    [Fact]
    public void JsonElement_equal_documents_are_equal()
    {
        // Arrange
        using var docA = JsonDocument.Parse("""{"a":1,"b":2}""");
        using var docB = JsonDocument.Parse("""{"a":1,"b":2}""");

        // Act
        var equal = MySqlJsonValueComparers.JsonElementComparer.Equals(docA.RootElement, docB.RootElement);

        // Assert
        Assert.True(equal);
    }

    /// <summary>JsonElement equals fails fast when canonical lengths differ.</summary>
    [Fact]
    public void JsonElement_different_length_documents_are_not_equal()
    {
        // Arrange
        using var docA = JsonDocument.Parse("""{"a":1}""");
        using var docB = JsonDocument.Parse("""{"a":1,"b":2}""");

        // Act
        var equal = MySqlJsonValueComparers.JsonElementComparer.Equals(docA.RootElement, docB.RootElement);

        // Assert
        Assert.False(equal);
    }

    /// <summary>JsonElement equals on two same-length but different documents.</summary>
    [Fact]
    public void JsonElement_same_length_different_content_are_not_equal()
    {
        // Arrange
        using var docA = JsonDocument.Parse("""{"a":1}""");
        using var docB = JsonDocument.Parse("""{"a":2}""");

        // Act
        var equal = MySqlJsonValueComparers.JsonElementComparer.Equals(docA.RootElement, docB.RootElement);

        // Assert
        Assert.False(equal);
    }

    /// <summary>JsonElement object equality follows JSON semantics rather than property order.</summary>
    [Fact]
    public void JsonElement_reordered_properties_are_equal_and_have_equal_hashes()
    {
        // Arrange
        using var docA = JsonDocument.Parse("""{"a":1,"b":{"c":2,"d":3}}""");
        using var docB = JsonDocument.Parse("""{"b":{"d":3,"c":2},"a":1}""");

        // Act
        var equal = MySqlJsonValueComparers.JsonElementComparer.Equals(docA.RootElement, docB.RootElement);
        var firstHash = MySqlJsonValueComparers.JsonElementComparer.GetHashCode(docA.RootElement);
        var secondHash = MySqlJsonValueComparers.JsonElementComparer.GetHashCode(docB.RootElement);

        // Assert
        Assert.True(equal);
        Assert.Equal(firstHash, secondHash);
    }

    /// <summary>Equivalent JSON number spellings remain equal and hash-compatible.</summary>
    [Fact]
    public void JsonElement_equivalent_number_representations_are_equal_and_have_equal_hashes()
    {
        // Arrange
        using var docA = JsonDocument.Parse("""{"value":10e-3}""");
        using var docB = JsonDocument.Parse("""{"value":0.01}""");

        // Act
        var equal = MySqlJsonValueComparers.JsonElementComparer.Equals(docA.RootElement, docB.RootElement);
        var firstHash = MySqlJsonValueComparers.JsonElementComparer.GetHashCode(docA.RootElement);
        var secondHash = MySqlJsonValueComparers.JsonElementComparer.GetHashCode(docB.RootElement);

        // Assert
        Assert.True(equal);
        Assert.Equal(firstHash, secondHash);
    }

    /// <summary>JsonElement equals on a payload larger than the initial 1 KB buffer triggers the grow path.</summary>
    [Fact]
    public void JsonElement_large_payload_triggers_buffer_grow()
    {
        // Arrange
        // WHY: Two hundred entries exceed the initial 1024-byte buffer and exercise growth.
        var large = "[" + string.Join(",", Enumerable.Range(0, 200).Select(i => $"\"item-{i:D6}\"")) + "]";
        using var docA = JsonDocument.Parse(large);
        using var docB = JsonDocument.Parse(large);

        // Act
        var equal = MySqlJsonValueComparers.JsonElementComparer.Equals(docA.RootElement, docB.RootElement);

        // Assert
        Assert.True(equal);
    }

    /// <summary>JsonElement hash is stable across two parses of the same JSON.</summary>
    [Fact]
    public void JsonElement_hash_is_stable_across_equal_documents()
    {
        // Arrange
        using var docA = JsonDocument.Parse("""{"a":1,"b":"x"}""");
        using var docB = JsonDocument.Parse("""{"a":1,"b":"x"}""");

        // Act
        var firstHash = MySqlJsonValueComparers.JsonElementComparer.GetHashCode(docA.RootElement);
        var secondHash = MySqlJsonValueComparers.JsonElementComparer.GetHashCode(docB.RootElement);

        // Assert
        Assert.Equal(firstHash, secondHash);
    }

    /// <summary>Same-reference JsonDocument is equal to itself.</summary>
    [Fact]
    public void JsonDocument_reference_equal_is_equal()
    {
        // Arrange
        using var doc = JsonDocument.Parse("""{"a":1}""");

        // Act
        var equal = MySqlJsonValueComparers.JsonDocumentComparer.Equals(doc, doc);

        // Assert
        Assert.True(equal);
    }

    /// <summary>JsonDocument equals returns false when one side is null.</summary>
    [Fact]
    public void JsonDocument_one_null_side_is_not_equal()
    {
        // Arrange
        using var doc = JsonDocument.Parse("""{"a":1}""");

        // Act
        var equal = MySqlJsonValueComparers.JsonDocumentComparer.Equals(doc, null);
        var reverseEqual = MySqlJsonValueComparers.JsonDocumentComparer.Equals(null, doc);

        // Assert
        Assert.False(equal);
        Assert.False(reverseEqual);
    }

    /// <summary>JsonDocument equals returns true when both sides are null.</summary>
    [Fact]
    public void JsonDocument_both_null_is_equal()
    {
        // Arrange
        JsonDocument? document = null;

        // Act
        var equal = MySqlJsonValueComparers.JsonDocumentComparer.Equals(document, document);

        // Assert
        Assert.True(equal);
    }

    /// <summary>JsonDocument hash for null returns zero.</summary>
    [Fact]
    public void JsonDocument_hash_for_null_is_zero()
    {
        // Arrange
        JsonDocument document = null!;

        // Act
        var hash = MySqlJsonValueComparers.JsonDocumentComparer.GetHashCode(document);

        // Assert
        Assert.Equal(0, hash);
    }

    /// <summary>Snapshot of null JsonDocument returns null.</summary>
    [Fact]
    public void JsonDocument_snapshot_of_null_is_null()
    {
        // Arrange
        JsonDocument? document = null;

        // Act
        var snapshot = MySqlJsonValueComparers.JsonDocumentComparer.Snapshot(document);

        // Assert
        Assert.Null(snapshot);
    }

    /// <summary>Snapshot of JsonDocument produces an equal but independent document.</summary>
    [Fact]
    public void JsonDocument_snapshot_round_trips()
    {
        // Arrange
        using var source = JsonDocument.Parse("""{"a":1,"b":[1,2,3]}""");

        // Act
        using var snapshot = MySqlJsonValueComparers.JsonDocumentComparer.Snapshot(source);

        // Assert
        Assert.NotSame(source, snapshot);
        Assert.True(MySqlJsonValueComparers.JsonDocumentComparer.Equals(source, snapshot));
    }

    /// <summary>
    /// Verifies a cloned document remains valid after its returned backing buffer is overwritten.
    /// </summary>
    [Fact]
    public void JsonDocument_snapshot_does_not_borrow_returned_pool_memory()
    {
        // Arrange
        var pool = new TrackingArrayPool();
        using var source = JsonDocument.Parse("""{"a":1,"b":[1,2,3]}""");

        // Act
        using var snapshot = MySqlJsonValueComparers.CloneJsonDocument(source, pool)!;
        var reusedBuffer = pool.Rent(1);

        try
        {
            Array.Fill(reusedBuffer, (byte)' ');

            // Assert
            Assert.Equal(1, snapshot.RootElement.GetProperty("a").GetInt32());
            Assert.Equal(3, snapshot.RootElement.GetProperty("b").GetArrayLength());
        }
        finally
        {
            pool.Return(reusedBuffer);
        }
    }

    /// <summary>
    /// Verifies equal extreme JSON number representations preserve compatible hashes.
    /// </summary>
    /// <param name="json">The extreme numeric payload to parse twice.</param>
    [Theory]
    [InlineData("1e100")]
    [InlineData("1e9999")]
    public void JsonElement_extreme_numbers_remain_equal_and_hash_compatible(
        string json
    )
    {
        // Arrange
        using var first = JsonDocument.Parse(json);
        using var second = JsonDocument.Parse(json);

        // Act
        var equal = MySqlJsonValueComparers.JsonElementComparer.Equals(first.RootElement, second.RootElement);
        var firstHash = MySqlJsonValueComparers.JsonElementComparer.GetHashCode(first.RootElement);
        var secondHash = MySqlJsonValueComparers.JsonElementComparer.GetHashCode(second.RootElement);

        // Assert
        Assert.True(equal);
        Assert.Equal(firstHash, secondHash);
    }
}
