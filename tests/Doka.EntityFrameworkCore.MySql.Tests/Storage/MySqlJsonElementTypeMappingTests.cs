using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Pins the ownership and parsing contract of the provider's
/// <see cref="JsonElement"/> value converter.
/// </summary>
public sealed class MySqlJsonElementTypeMappingTests
{
    private static readonly ValueConverter s_converter =
        MySqlJsonTypeMapping.CreateJsonElementMapping().Converter!;

    /// <summary>
    /// Verifies converter returns an independently owned element.
    /// </summary>
    [Fact]
    public void Converter_returns_an_independently_owned_element()
    {
        // Arrange: The class-level converter is the test subject.

        // Act
        var actual = s_converter.ConvertFromProviderExpression.Body;

        // Assert
        var parseCall = Assert.IsAssignableFrom<MethodCallExpression>(
            actual);

        Assert.Equal(typeof(JsonElement), parseCall.Method.DeclaringType);
        Assert.Equal(nameof(JsonElement.Parse), parseCall.Method.Name);
    }

    /// <summary>
    /// Verifies converter materializes valid json.
    /// </summary>
    [Theory]
    [InlineData("null", JsonValueKind.Null)]
    [InlineData("42", JsonValueKind.Number)]
    [InlineData("[1,2,3]", JsonValueKind.Array)]
    [InlineData("{\"key\":\"value\"}", JsonValueKind.Object)]
    public void Converter_materializes_valid_json(
        string json,
        JsonValueKind expectedValueKind
    )
    {
        // Arrange: The theory supplies json, expectedValueKind.

        // Act
        var actual = s_converter.ConvertFromProvider(json);

        // Assert
        var element = Assert.IsType<JsonElement>(actual);
        Assert.Equal(expectedValueKind, element.ValueKind);
        Assert.Equal(json, element.GetRawText());
    }

    /// <summary>
    /// Verifies converter rejects malformed json.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{\"key\":1} trailing")]
    public void Converter_rejects_malformed_json(
        string json
    )
    {
        // Arrange
        Action act = () => s_converter.ConvertFromProvider(json);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsAssignableFrom<JsonException>(recordedException);
    }

    /// <summary>
    /// Verifies native json mappings generate executable model value literals.
    /// </summary>
    [Theory]
    [InlineData(typeof(JsonElement))]
    [InlineData(typeof(JsonDocument))]
    [InlineData(typeof(JsonNode))]
    [InlineData(typeof(JsonObject))]
    [InlineData(typeof(JsonArray))]
    public void Native_json_mappings_generate_executable_model_value_literals(
        Type jsonType
    )
    {
        // Arrange
        var mapping = CreateMapping(jsonType);
        var json = jsonType == typeof(JsonArray) ? """["array"]""" : """{"kind":"literal"}""";
        var value = jsonType == typeof(JsonElement)
            ? (object)JsonElement.Parse(json)
            : jsonType == typeof(JsonDocument)
                ? JsonDocument.Parse(json)
                : JsonNode.Parse(json)!;

        using var sourceDocument = value as IDisposable;

        // Act
        var generatedValue = Expression
            .Lambda(mapping.GenerateCodeLiteral(value))
            .Compile()
            .DynamicInvoke();

        using var generatedDocument = generatedValue as IDisposable;

        // Assert
        Assert.NotNull(generatedValue);
        Assert.True(jsonType.IsAssignableFrom(generatedValue.GetType()));
    }

    /// <summary>
    /// Verifies native json mappings reject non json code literal values.
    /// </summary>
    [Theory]
    [InlineData(typeof(JsonElement))]
    [InlineData(typeof(JsonDocument))]
    [InlineData(typeof(JsonNode))]
    [InlineData(typeof(JsonObject))]
    [InlineData(typeof(JsonArray))]
    public void Native_json_mappings_reject_non_json_code_literal_values(
        Type jsonType
    )
    {
        // Arrange
        var mapping = CreateMapping(jsonType);

        // Act
        var exception = Record.Exception(() => mapping.GenerateCodeLiteral(42));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    /// <summary>Creates the provider mapping matching one supported JSON model type.</summary>
    private static RelationalTypeMapping CreateMapping(Type jsonType) => jsonType switch
    {
        _ when jsonType == typeof(JsonElement) => MySqlJsonTypeMapping.CreateJsonElementMapping(),
        _ when jsonType == typeof(JsonDocument) => MySqlJsonTypeMapping.CreateJsonDocumentMapping(),
        _ when jsonType == typeof(JsonNode) => MySqlJsonTypeMapping.CreateJsonNodeMapping(),
        _ when jsonType == typeof(JsonObject) => MySqlJsonTypeMapping.CreateJsonObjectMapping(),
        _ when jsonType == typeof(JsonArray) => MySqlJsonTypeMapping.CreateJsonArrayMapping(),
        _ => throw new ArgumentOutOfRangeException(nameof(jsonType)),
    };
}
