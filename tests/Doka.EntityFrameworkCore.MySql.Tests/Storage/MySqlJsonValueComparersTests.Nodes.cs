using System.Text.Json.Nodes;

namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlJsonValueComparersTests
{
    /// <summary>Same-reference JsonNode is equal to itself.</summary>
    [Fact]
    public void JsonNode_reference_equal_is_equal()
    {
        // Arrange
        var node = JsonNode.Parse("""{"a":1}""");

        // Act
        var equal = MySqlJsonValueComparers.JsonNodeComparer.Equals(node, node);

        // Assert
        Assert.True(equal);
    }

    /// <summary>JsonNode equals returns false when one side is null.</summary>
    [Fact]
    public void JsonNode_one_null_side_is_not_equal()
    {
        // Arrange
        var node = JsonNode.Parse("""{"a":1}""");

        // Act
        var equal = MySqlJsonValueComparers.JsonNodeComparer.Equals(node, null);
        var reverseEqual = MySqlJsonValueComparers.JsonNodeComparer.Equals(null, node);

        // Assert
        Assert.False(equal);
        Assert.False(reverseEqual);
    }

    /// <summary>JsonNode equals returns true when both sides are null.</summary>
    [Fact]
    public void JsonNode_both_null_is_equal()
    {
        // Arrange
        JsonNode? node = null;

        // Act
        var equal = MySqlJsonValueComparers.JsonNodeComparer.Equals(node, node);

        // Assert
        Assert.True(equal);
    }

    /// <summary>JsonNode equals on two parses of the same JSON.</summary>
    [Fact]
    public void JsonNode_equal_payloads_are_equal()
    {
        // Arrange
        var a = JsonNode.Parse("""{"a":1,"b":2}""");
        var b = JsonNode.Parse("""{"a":1,"b":2}""");

        // Act
        var equal = MySqlJsonValueComparers.JsonNodeComparer.Equals(a, b);

        // Assert
        Assert.True(equal);
    }

    /// <summary>
    /// Verifies one semantic equality pair is symmetric and preserves hash compatibility.
    /// </summary>
    /// <param name="caseName">The named pair of independently constructed JSON representations.</param>
    [Theory]
    [MemberData(nameof(SemanticallyEqualNodeCases))]
    public void JsonNode_semantically_equal_values_are_symmetric_and_hash_compatible(
        string caseName
    )
    {
        // Arrange
        var (left, right) = CreateNodePair(caseName);

        // Act
        var equal = MySqlJsonValueComparers.JsonNodeComparer.Equals(left, right);
        var reverseEqual = MySqlJsonValueComparers.JsonNodeComparer.Equals(right, left);
        var leftHash = MySqlJsonValueComparers.JsonNodeComparer.GetHashCode(left!);
        var rightHash = MySqlJsonValueComparers.JsonNodeComparer.GetHashCode(right!);

        // Assert
        Assert.True(equal);
        Assert.True(reverseEqual);
        Assert.Equal(leftHash, rightHash);
    }

    /// <summary>
    /// Verifies one distinct payload pair remains unequal in both operand orders.
    /// </summary>
    /// <param name="caseName">The named semantic difference to compare.</param>
    [Theory]
    [MemberData(nameof(SemanticallyDistinctNodeCases))]
    public void JsonNode_semantically_distinct_values_are_symmetric(
        string caseName
    )
    {
        // Arrange
        var (left, right) = CreateNodePair(caseName);

        // Act
        var equal = MySqlJsonValueComparers.JsonNodeComparer.Equals(left, right);
        var reverseEqual = MySqlJsonValueComparers.JsonNodeComparer.Equals(right, left);

        // Assert
        Assert.False(equal);
        Assert.False(reverseEqual);
    }

    /// <summary>
    /// Verifies case-insensitive property names preserve symmetric equality and hashing.
    /// </summary>
    [Fact]
    public void JsonNode_case_insensitive_objects_remain_symmetric_and_hash_compatible()
    {
        // Arrange
        var options = new JsonNodeOptions { PropertyNameCaseInsensitive = true };
        var left = new JsonObject(options) { ["Name"] = 1 };
        var right = new JsonObject(options) { ["name"] = 1L };

        // Act
        var equal = MySqlJsonValueComparers.JsonNodeComparer.Equals(left, right);
        var reverseEqual = MySqlJsonValueComparers.JsonNodeComparer.Equals(right, left);
        var leftHash = MySqlJsonValueComparers.JsonNodeComparer.GetHashCode(left);
        var rightHash = MySqlJsonValueComparers.JsonNodeComparer.GetHashCode(right);

        // Assert
        Assert.True(equal);
        Assert.True(reverseEqual);
        Assert.Equal(leftHash, rightHash);
    }

    /// <summary>
    /// Verifies different property-name comparison options cannot produce asymmetric equality.
    /// </summary>
    [Fact]
    public void JsonNode_objects_with_different_name_comparison_options_remain_symmetric()
    {
        // Arrange
        var sensitive = new JsonObject { ["Name"] = 1 };
        var insensitive = new JsonObject(
            new JsonNodeOptions { PropertyNameCaseInsensitive = true })
        {
            ["name"] = 1L,
        };

        // Act
        var equal = MySqlJsonValueComparers.JsonNodeComparer.Equals(sensitive, insensitive);
        var reverseEqual = MySqlJsonValueComparers.JsonNodeComparer.Equals(insensitive, sensitive);

        // Assert
        Assert.False(equal);
        Assert.False(reverseEqual);
    }

    /// <summary>
    /// Verifies nested objects retain symmetric equality when their name-comparison options differ.
    /// </summary>
    [Fact]
    public void JsonNode_nested_objects_with_different_name_comparison_options_remain_symmetric()
    {
        // Arrange
        var sensitive = new JsonArray(
            new JsonObject { ["Name"] = 1 });

        var insensitive = new JsonArray(
            new JsonObject(new JsonNodeOptions { PropertyNameCaseInsensitive = true })
            {
                ["name"] = 1L,
            });

        // Act
        var equal = MySqlJsonValueComparers.JsonNodeComparer.Equals(sensitive, insensitive);
        var reverseEqual = MySqlJsonValueComparers.JsonNodeComparer.Equals(insensitive, sensitive);

        // Assert
        Assert.False(equal);
        Assert.False(reverseEqual);
    }

    /// <summary>
    /// Verifies a reparented child retains the comparer of its already materialized property dictionary.
    /// </summary>
    [Fact]
    public void JsonNode_reparented_object_uses_materialized_dictionary_comparer_symmetrically()
    {
        // Arrange
        var insensitive = new JsonNodeOptions { PropertyNameCaseInsensitive = true };
        var materializedSensitiveChild = new JsonObject { ["Name"] = 1 };
        var left = new JsonObject(insensitive) { ["child"] = materializedSensitiveChild };
        var right = new JsonObject(insensitive)
        {
            ["child"] = new JsonObject(insensitive) { ["name"] = 1L },
        };

        var expected = JsonNode.DeepEquals(left, right) && JsonNode.DeepEquals(right, left);

        // Act
        var equal = MySqlJsonValueComparers.JsonNodeComparer.Equals(left, right);
        var reverseEqual = MySqlJsonValueComparers.JsonNodeComparer.Equals(right, left);

        // Assert
        Assert.False(expected);
        Assert.Equal(expected, equal);
        Assert.Equal(expected, reverseEqual);
    }

    /// <summary>
    /// Verifies the one-pass comparer matches the bidirectional JSON equality contract for one pair.
    /// </summary>
    /// <param name="caseName">The named representation or comparison-option boundary.</param>
    [Theory]
    [MemberData(nameof(BidirectionalNodeCases))]
    public void JsonNode_one_pass_equality_matches_bidirectional_deep_equals(
        string caseName
    )
    {
        // Arrange
        var (left, right) = CreateNodePair(caseName);
        var expected = JsonNode.DeepEquals(left, right) && JsonNode.DeepEquals(right, left);

        // Act
        var equal = MySqlJsonValueComparers.JsonNodeComparer.Equals(left, right);
        var reverseEqual = MySqlJsonValueComparers.JsonNodeComparer.Equals(right, left);

        // Assert
        Assert.Equal(expected, equal);
        Assert.Equal(expected, reverseEqual);
    }

    /// <summary>
    /// Verifies one primitive representation pair matches bidirectional equality and equal-value hashing.
    /// </summary>
    /// <param name="leftRepresentation">The representation used for the left operand.</param>
    /// <param name="rightRepresentation">The representation used for the right operand.</param>
    [Theory]
    [MemberData(nameof(PrimitiveNodeCases))]
    public void JsonNode_primitive_equality_and_hashing_match_bidirectional_deep_equals_for_supported_types(
        string leftRepresentation,
        string rightRepresentation
    )
    {
        // Arrange
        var left = CreatePrimitiveNode(leftRepresentation);
        var right = CreatePrimitiveNode(rightRepresentation);
        var expected = JsonNode.DeepEquals(left, right) && JsonNode.DeepEquals(right, left);

        // Act
        var equal = MySqlJsonValueComparers.JsonNodeComparer.Equals(left, right);
        var reverseEqual = MySqlJsonValueComparers.JsonNodeComparer.Equals(right, left);
        var leftHash = expected ? MySqlJsonValueComparers.JsonNodeComparer.GetHashCode(left) : (int?)null;
        var rightHash = expected ? MySqlJsonValueComparers.JsonNodeComparer.GetHashCode(right) : (int?)null;

        // Assert
        Assert.Equal(expected, equal);
        Assert.Equal(expected, reverseEqual);
        if (expected)
        {
            Assert.Equal(leftHash, rightHash);
        }
    }

    /// <summary>
    /// Verifies one container boundary matches bidirectional equality and equal-value hashing.
    /// </summary>
    /// <param name="caseName">The named null, shape, or length boundary.</param>
    [Theory]
    [MemberData(nameof(ContainerEdgeNodeCases))]
    public void JsonNode_container_edges_match_bidirectional_deep_equals(
        string caseName
    )
    {
        // Arrange
        var (left, right) = CreateNodePair(caseName);
        var expected = JsonNode.DeepEquals(left, right) && JsonNode.DeepEquals(right, left);

        // Act
        var equal = MySqlJsonValueComparers.JsonNodeComparer.Equals(left, right);
        var reverseEqual = MySqlJsonValueComparers.JsonNodeComparer.Equals(right, left);
        var leftHash = expected ? MySqlJsonValueComparers.JsonNodeComparer.GetHashCode(left!) : (int?)null;
        var rightHash = expected ? MySqlJsonValueComparers.JsonNodeComparer.GetHashCode(right!) : (int?)null;

        // Assert
        Assert.Equal(expected, equal);
        Assert.Equal(expected, reverseEqual);
        if (expected)
        {
            Assert.Equal(leftHash, rightHash);
        }
    }

    /// <summary>
    /// Verifies distinct numeric values retain useful hash distribution.
    /// </summary>
    [Fact]
    public void JsonNode_numeric_values_contribute_to_hash_distribution()
    {
        // Arrange
        var nodes = Enumerable
            .Range(1, 5)
            .Select(index => JsonNode.Parse($$$"""{"id":{{{index}}},"name":"item"}""")!)
            .ToArray();

        // Act
        var hashes = nodes
            .Select(MySqlJsonValueComparers.JsonNodeComparer.GetHashCode)
            .Distinct()
            .Count();

        // Assert
        Assert.True(hashes >= 4);
    }

    /// <summary>
    /// Verifies a customized numeric JSON value is symmetric and hash-compatible with its built-in equivalent.
    /// </summary>
    [Fact]
    public void JsonNode_customized_numeric_value_is_hash_compatible_with_builtin_number()
    {
        // Arrange
        var customized = JsonValue.Create(CustomNumber.One)!;
        var builtin = JsonValue.Create(1)!;

        // Act
        var equal = MySqlJsonValueComparers.JsonNodeComparer.Equals(customized, builtin);
        var reverseEqual = MySqlJsonValueComparers.JsonNodeComparer.Equals(builtin, customized);
        var customizedHash = MySqlJsonValueComparers.JsonNodeComparer.GetHashCode(customized);
        var builtinHash = MySqlJsonValueComparers.JsonNodeComparer.GetHashCode(builtin);

        // Assert
        Assert.True(equal);
        Assert.True(reverseEqual);
        Assert.Equal(customizedHash, builtinHash);
    }

    /// <summary>
    /// Verifies one customized container representation preserves semantic equality and hashing.
    /// </summary>
    /// <param name="caseName">The named object or array representation.</param>
    [Theory]
    [MemberData(nameof(CustomizedContainerNodeCases))]
    public void JsonNode_customized_object_and_array_values_are_hash_compatible(
        string caseName
    )
    {
        // Arrange
        var (left, right) = CreateNodePair(caseName);

        // Act
        var equal = MySqlJsonValueComparers.JsonNodeComparer.Equals(left, right);
        var reverseEqual = MySqlJsonValueComparers.JsonNodeComparer.Equals(right, left);
        var leftHash = MySqlJsonValueComparers.JsonNodeComparer.GetHashCode(left!);
        var rightHash = MySqlJsonValueComparers.JsonNodeComparer.GetHashCode(right!);

        // Assert
        Assert.True(equal);
        Assert.True(reverseEqual);
        Assert.Equal(leftHash, rightHash);
    }

    /// <summary>JsonNode equals fails fast when serialized lengths differ.</summary>
    [Fact]
    public void JsonNode_different_length_payloads_are_not_equal()
    {
        // Arrange
        var a = JsonNode.Parse("""{"a":1}""");
        var b = JsonNode.Parse("""{"a":1,"b":2}""");

        // Act
        var equal = MySqlJsonValueComparers.JsonNodeComparer.Equals(a, b);

        // Assert
        Assert.False(equal);
    }

    /// <summary>JsonNode hash for null returns zero.</summary>
    [Fact]
    public void JsonNode_hash_for_null_is_zero()
    {
        // Arrange
        JsonNode node = null!;

        // Act
        var hash = MySqlJsonValueComparers.JsonNodeComparer.GetHashCode(node);

        // Assert
        Assert.Equal(0, hash);
    }

    /// <summary>Snapshot of null JsonNode returns null.</summary>
    [Fact]
    public void JsonNode_snapshot_of_null_is_null()
    {
        // Arrange
        JsonNode? node = null;

        // Act
        var snapshot = MySqlJsonValueComparers.JsonNodeComparer.Snapshot(node);

        // Assert
        Assert.Null(snapshot);
    }

    /// <summary>Snapshot of JsonNode produces an equal but independent node.</summary>
    [Fact]
    public void JsonNode_snapshot_round_trips()
    {
        // Arrange
        var source = JsonNode.Parse("""{"a":1,"b":[1,2,3]}""");

        // Act
        var snapshot = MySqlJsonValueComparers.JsonNodeComparer.Snapshot(source);

        // Assert
        Assert.NotSame(source, snapshot);
        Assert.True(MySqlJsonValueComparers.JsonNodeComparer.Equals(source, snapshot));
    }

    /// <summary>
    /// Verifies mutating nested values in a snapshot cannot change the source node.
    /// </summary>
    [Fact]
    public void JsonNode_snapshot_is_deeply_independent()
    {
        // Arrange
        var source = JsonNode.Parse("""{"nested":{"value":1},"items":[1,2]}""")!;
        var snapshot = MySqlJsonValueComparers.JsonNodeComparer.Snapshot(source)!;

        // Act
        snapshot["nested"]!["value"] = 2;
        snapshot["items"]!.AsArray().Add(3);

        // Assert
        Assert.Equal(1, source["nested"]!["value"]!.GetValue<int>());
        Assert.Equal(2, source["items"]!.AsArray().Count);
        Assert.False(MySqlJsonValueComparers.JsonNodeComparer.Equals(source, snapshot));
    }

    private enum CustomNumber
    {
        One = 1,
    }
}
