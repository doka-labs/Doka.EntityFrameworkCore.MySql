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

    [Fact]
    public void Converter_returns_an_independently_owned_element()
    {
        var parseCall = Assert.IsAssignableFrom<MethodCallExpression>(
            s_converter.ConvertFromProviderExpression.Body);

        Assert.Equal(typeof(JsonElement), parseCall.Method.DeclaringType);
        Assert.Equal(nameof(JsonElement.Parse), parseCall.Method.Name);
    }

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
        var element = Assert.IsType<JsonElement>(s_converter.ConvertFromProvider(json));

        Assert.Equal(expectedValueKind, element.ValueKind);
        Assert.Equal(json, element.GetRawText());
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{\"key\":1} trailing")]
    public void Converter_rejects_malformed_json(
        string json
    ) => Assert.ThrowsAny<JsonException>(() => s_converter.ConvertFromProvider(json));
}
