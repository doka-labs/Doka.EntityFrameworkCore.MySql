using System.Text.Json.Nodes;

namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlJsonValueComparersTests
{
    private static readonly Guid s_jsonGuid = Guid.Parse("ebde3793-3e08-4a89-aa39-76b92c1c4c72");
    private static readonly DateTime s_jsonTimestamp = new(2026, 8, 22, 12, 34, 56, DateTimeKind.Utc);
    private static readonly string[] s_primitiveRepresentations =
    [
        "parsed-integer", "parsed-decimal", "byte", "sbyte", "int16", "uint16", "int32", "uint32",
        "int64", "uint64", "half", "single", "double", "decimal", "int128", "uint128", "parsed-boolean",
        "boolean", "parsed-text", "text", "parsed-character", "character", "parsed-guid", "guid",
        "parsed-timestamp", "timestamp",
    ];

    /// <summary>
    /// Supplies named semantic equality pairs without constructing mutable nodes during discovery.
    /// </summary>
    public static TheoryData<string> SemanticallyEqualNodeCases => new()
    {
        "reordered-properties",
        "equivalent-number-spellings",
        "parsed-and-custom-object",
        "int32-and-int64",
        "escaped-and-created-text",
        "guid-and-text",
    };

    /// <summary>
    /// Supplies independently selectable semantic differences.
    /// </summary>
    public static TheoryData<string> SemanticallyDistinctNodeCases => new()
    {
        "array-order",
        "missing-and-null-property",
        "number-and-text",
        "nested-value-difference",
    };

    /// <summary>
    /// Supplies representation and comparison-option boundaries for bidirectional JSON equality.
    /// </summary>
    public static TheoryData<string> BidirectionalNodeCases => new()
    {
        "reordered-properties",
        "value-difference",
        "mixed-case-options-exact-name",
        "mixed-case-options-different-name",
        "insensitive-property-name",
        "nested-mixed-case-options",
        "duplicate-sensitive-names",
        "array-element-order",
        "equivalent-number-spellings",
        "int32-and-int64",
        "customized-object",
        "customized-array",
        "true-and-null",
        "both-null",
    };

    /// <summary>
    /// Supplies the complete Cartesian product of the supported primitive representations.
    /// </summary>
    public static IEnumerable<object[]> PrimitiveNodeCases()
    {
        foreach (var left in s_primitiveRepresentations)
        {
            foreach (var right in s_primitiveRepresentations)
            {
                yield return [left, right];
            }
        }
    }

    /// <summary>
    /// Supplies the null-element, container-shape, and length boundaries independently.
    /// </summary>
    public static TheoryData<string> ContainerEdgeNodeCases => new()
    {
        "array-null-elements",
        "array-null-and-number",
        "object-and-array",
        "object-and-boolean",
        "array-and-object",
        "array-and-boolean",
        "array-length",
    };

    /// <summary>
    /// Supplies customized object and array representations independently.
    /// </summary>
    public static TheoryData<string> CustomizedContainerNodeCases => new()
    {
        "customized-object",
        "customized-array",
    };

    /// <summary>
    /// Constructs fresh operands so discovery and parallel case execution cannot share mutable JSON nodes.
    /// </summary>
    private static (JsonNode? Left, JsonNode? Right) CreateNodePair(
        string caseName
    )
    {
        var insensitive = new JsonNodeOptions { PropertyNameCaseInsensitive = true };

        return caseName switch
        {
            "reordered-properties" => (
                JsonNode.Parse("""{"a":1,"nested":{"b":2,"c":3}}"""),
                JsonNode.Parse("""{"nested":{"c":3,"b":2},"a":1}""")),
            "equivalent-number-spellings" => (JsonNode.Parse("10e-3"), JsonNode.Parse("0.01")),
            "parsed-and-custom-object" => (
                JsonNode.Parse("""{"number":1,"text":"value"}"""),
                new JsonObject { ["number"] = 1L, ["text"] = "value" }),
            "int32-and-int64" => (JsonValue.Create(1), JsonValue.Create(1L)),
            "escaped-and-created-text" => (JsonNode.Parse("\"\\u0076alue\""), JsonValue.Create("value")),
            "guid-and-text" => (JsonValue.Create(s_jsonGuid), JsonValue.Create(s_jsonGuid.ToString())),
            "array-order" => (JsonNode.Parse("[1,2]"), JsonNode.Parse("[2,1]")),
            "missing-and-null-property" => (JsonNode.Parse("""{"value":null}"""), JsonNode.Parse("{}")),
            "number-and-text" => (JsonValue.Create(1), JsonValue.Create("1")),
            "nested-value-difference" => (
                JsonNode.Parse("""{"nested":{"value":1}}"""),
                JsonNode.Parse("""{"nested":{"value":2}}""")),
            "value-difference" => (JsonNode.Parse("""{"value":1}"""), JsonNode.Parse("""{"value":2}""")),
            "mixed-case-options-exact-name" => (
                new JsonObject { ["Name"] = 1 },
                new JsonObject(insensitive) { ["Name"] = 1L }),
            "mixed-case-options-different-name" => (
                new JsonObject { ["Name"] = 1 },
                new JsonObject(insensitive) { ["name"] = 1L }),
            "insensitive-property-name" => (
                new JsonObject(insensitive) { ["Name"] = 1 },
                new JsonObject(insensitive) { ["name"] = 1L }),
            "nested-mixed-case-options" => (
                new JsonArray(new JsonObject { ["Name"] = 1 }),
                new JsonArray(new JsonObject(insensitive) { ["name"] = 1L })),
            "duplicate-sensitive-names" => (
                new JsonObject { ["A"] = 1, ["a"] = 2 },
                new JsonObject(insensitive) { ["A"] = 1, ["B"] = 2 }),
            "array-element-order" => (JsonNode.Parse("[1,2,3]"), JsonNode.Parse("[1,3,2]")),
            "customized-object" => (
                JsonValue.Create(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }),
                JsonNode.Parse("""{"b":2,"a":1}""")),
            "customized-array" => (JsonValue.Create(new List<int> { 1, 2, 3 }), JsonNode.Parse("[1,2,3]")),
            "true-and-null" => (JsonNode.Parse("true"), null),
            "both-null" => (null, null),
            "array-null-elements" => (new JsonArray { null }, new JsonArray { null }),
            "array-null-and-number" => (new JsonArray { null }, new JsonArray { 1 }),
            "object-and-array" => (JsonNode.Parse("{}"), JsonNode.Parse("[]")),
            "object-and-boolean" => (JsonNode.Parse("{}"), JsonNode.Parse("true")),
            "array-and-object" => (JsonNode.Parse("[]"), JsonNode.Parse("{}")),
            "array-and-boolean" => (JsonNode.Parse("[]"), JsonNode.Parse("true")),
            "array-length" => (JsonNode.Parse("[1]"), JsonNode.Parse("[1,2]")),
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, "Unknown JSON comparison pair."),
        };
    }

    private static JsonNode CreatePrimitiveNode(
        string representation
    ) => representation switch
    {
        "parsed-integer" => JsonNode.Parse("1")!,
        "parsed-decimal" => JsonNode.Parse("1.0")!,
        "byte" => JsonValue.Create((byte)1)!,
        "sbyte" => JsonValue.Create((sbyte)1)!,
        "int16" => JsonValue.Create((short)1)!,
        "uint16" => JsonValue.Create((ushort)1)!,
        "int32" => JsonValue.Create(1)!,
        "uint32" => JsonValue.Create(1U)!,
        "int64" => JsonValue.Create(1L)!,
        "uint64" => JsonValue.Create(1UL)!,
        "half" => JsonValue.Create((Half)1)!,
        "single" => JsonValue.Create(1F)!,
        "double" => JsonValue.Create(1D)!,
        "decimal" => JsonValue.Create(1M)!,
        "int128" => JsonValue.Create((Int128)1)!,
        "uint128" => JsonValue.Create((UInt128)1)!,
        "parsed-boolean" => JsonNode.Parse("true")!,
        "boolean" => JsonValue.Create(true)!,
        "parsed-text" => JsonNode.Parse("\"value\"")!,
        "text" => JsonValue.Create("value")!,
        "parsed-character" => JsonNode.Parse("\"v\"")!,
        "character" => JsonValue.Create('v')!,
        "parsed-guid" => JsonNode.Parse($"\"{s_jsonGuid}\"")!,
        "guid" => JsonValue.Create(s_jsonGuid)!,
        "parsed-timestamp" => JsonNode.Parse($"\"{s_jsonTimestamp:O}\"")!,
        "timestamp" => JsonValue.Create(s_jsonTimestamp)!,
        _ => throw new ArgumentOutOfRangeException(nameof(representation), representation, "Unknown JSON primitive."),
    };
}
